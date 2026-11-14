using Sprache;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("Dbg")]
namespace TruckLib.Sii
{
    internal static class ParserElements
    {
        internal const string SiiHeader = "SiiNunit";
        internal const char StringDelimiterChar = '"';
        internal const char TupleOpenChar = '(';
        internal const char TupleCloseChar = ')';
        internal const char MatTupleOpenChar = '{';
        internal const char MatTupleCloseChar = '}';
        internal const char TupleSeparatorChar = ',';
        internal const char QuaternionWSeparatorChar = ';';
        internal const string True = "true";
        internal const string False = "false";
        private static readonly CultureInfo culture = CultureInfo.InvariantCulture;

        internal static readonly Parser<char> OpenCurly =
            Parse.Char('{').Lexeme();

        internal static readonly Parser<char> CloseCurly =
            Parse.Char('}').Lexeme();

        // A line terminator can close a string, as covered by
        // ParseStringWithMissingEndQuote.
        internal static readonly Parser<char> StringDelimiter =
            Parse.Char(StringDelimiterChar).Lexeme()
            .Or(Parse.LineTerminator.Return('"'));

        internal static readonly Parser<object> DelimitedString =
            from _ in Parse.Char(StringDelimiterChar).Lexeme()
            from s in Scan(c => c is not ('"' or '\r' or '\n'), "string content", minimum: 0)
            from __ in StringDelimiter
            select (object)s.TrimEnd();

        private static readonly Parser<string> Digits = Scan(c => c is >= '0' and <= '9', "digit");
        internal static readonly Parser<IOption<char>> Sign =
            Parse.Chars("+-").Optional().Lexeme();

        internal static readonly Parser<string> ExponentPart =
            from _ in Parse.Char('e')
            from s in Sign
            from d in Digits
            select "e" + (s.IsDefined ? s.Get().ToString() : "") + d;

        internal static readonly Parser<string> DotAndFractionalDigits =
            from dot in Parse.Char('.')
            from d in Scan(c => c is >= '0' and <= '9', "digit", minimum: 0)
            select '.' + d;

        private readonly record struct Number(object Integer, float Floating, bool HasFloat);
        // A number and a tuple are each read once, rather than trying every type
        // and vector dimension from the same position.
        private static readonly Parser<Number> Decimal =
            from sign in Sign
            from digits in Digits
            from fraction in DotAndFractionalDigits.Optional()
            from exponent in ExponentPart.Optional()
            from suffix in Parse.Char('f').Optional()
            select MakeNumber((sign.IsDefined ? sign.Get().ToString() : "") + digits
                + fraction.GetOrDefault() + exponent.GetOrDefault(),
                fraction.IsDefined || exponent.IsDefined || suffix.IsDefined);
        private static readonly Parser<Number> HexInteger =
            from prefix in Parse.String("0x")
            from digits in Scan(IsHex, "hex digit")
            select new Number(ParseInt(digits, NumberStyles.HexNumber), 0, false);
        private static readonly Parser<Number> FloatInHexNotation =
            from ampersand in Parse.Char('&')
            from digits in Scan(IsHex, "hex digit", minimum: 8, maximum: 8)
            select new Number(null, BitConverter.Int32BitsToSingle(int.Parse(digits, NumberStyles.HexNumber, culture)), true);
        private static readonly Parser<Number> Numeric = input =>
            !input.AtEnd && input.Current == '&' ? FloatInHexNotation(input)
            : input.Source.AsSpan(input.Position).StartsWith("0x".AsSpan(), StringComparison.Ordinal)
                ? HexInteger(input) : Decimal(input);
        private static readonly Parser<string> HorizontalSpace = Scan(c => c is ' ' or '\t', "horizontal whitespace", minimum: 0);
        private static readonly Parser<object> ScalarNumber =
            from number in Numeric
            from space in HorizontalSpace
            from line in Parse.LineTerminator
            select number.Integer ?? (object)number.Floating;

        private readonly record struct Component(Number Number, IOption<char> Separator);
        private readonly record struct TupleValue(Component[] Components, bool Mat)
        {
            internal bool Semicolon => Components.Any(c => c.Separator.IsDefined && c.Separator.Get() == ';');
            internal bool HasFloat => Components.All(c => c.Number.HasFloat);
        }
        private static readonly Parser<Component> TupleComponent =
            from value in Numeric.Lexeme()
            from separator in Parse.Chars(",;").Optional().Lexeme()
            select new Component(value, separator);
        private static readonly Parser<TupleValue> RoundTuple =
            from open in Parse.Char(TupleOpenChar).Lexeme()
            from values in TupleComponent.Many()
            from close in Parse.Char(TupleCloseChar).Lexeme()
            select new TupleValue(values.ToArray(), false);
        private static readonly Parser<TupleValue> MatTuple =
            from open in Parse.Char(MatTupleOpenChar).Lexeme()
            from values in TupleComponent.Many()
            from close in Parse.Char(MatTupleCloseChar).Lexeme()
            select new TupleValue(values.ToArray(), true);
        private static readonly Parser<TupleValue> Rotation = RoundTuple.Where(t => t.Components.Length == 4 && t.Semicolon && t.HasFloat);
        private static readonly Parser<TupleValue?> OptionalRotation = Rotation.Optional().Select(o => o.IsDefined ? (TupleValue?)o.Get() : null);
        private static readonly Parser<object> RoundValue =
            from tuple in RoundTuple
            from rotation in (tuple.Components.Length == 3 && !tuple.Semicolon && tuple.HasFloat
                ? OptionalRotation : Parse.Return<TupleValue?>(null))
            select rotation.HasValue ? new Placement(ToVector3(tuple), ToQuaternion(rotation.Value)) : ToValue(tuple);
        private static readonly Parser<object> MatValue = MatTuple.Select(ToValue);
        private static readonly Parser<string> Word = Scan(c => !char.IsWhiteSpace(c), "reference");
        private static readonly Parser<object> WordValue =
            from word in Word
            from line in (from space in HorizontalSpace
            from ending in Parse.LineTerminator select true).Optional()
            select word == "true" ? (object)true : word == "false" ? false
                : line.IsDefined && word.Length <= 12 && word.All(c => TruckLib.Token.CharacterSet.Contains(c)) ? new Token(word)
                : word[0] == '.' ? new OwnerPointer(word) : new LinkPointer(word);
        internal static readonly Parser<char> Colon =
            Parse.Char(':').Lexeme();

        internal static readonly Parser<string> KeyText = Scan(c => !char.IsWhiteSpace(c) && c is not (':' or '}' or '['), "key").Lexeme();

        internal static readonly Parser<string> KeyArrayIndex =
            from _ in Parse.Char('[').Lexeme()
            from val in Scan(c => c is >= '0' and <= '9', "digit", minimum: 0)
            from __ in Parse.Char(']').Lexeme()
            select $"[{val}]";

        private static readonly Parser<object> NumericOrReference = ScalarNumber.Or(WordValue);
        private static readonly Parser<object> HexFloatValue = FloatInHexNotation.Select(n => (object)n.Floating);
        internal static readonly Parser<object> Value = input =>
        {
            if (input.AtEnd) return WordValue(input);
            return input.Current switch
            {
                '"' => DelimitedString(input), '(' => RoundValue(input), '{' => MatValue(input), '&' => HexFloatValue(input),
                >= '0' and <= '9' or '+' or '-' => NumericOrReference(input), _ => WordValue(input)
            };
        };
        internal static readonly Parser<KeyValuePair<string, dynamic>> Pair =
            from k in KeyText
            from index in KeyArrayIndex.Optional()
            from _ in Colon
            from v in Value.Lexeme()
            select new KeyValuePair<string, dynamic>(k + index.GetOrDefault(), v);

        internal static readonly Parser<string> ClassName = Scan(c => !char.IsWhiteSpace(c) && c is not (':' or '}'), "class name").Lexeme();

        private static readonly Parser<string> ClassNameSuffix =
            from separator in Parse.String("::").Lexeme()
            from name in ClassName select "::" + name;
        internal static readonly Parser<string> UnitName = Scan(c => !char.IsWhiteSpace(c) && c is not ('{' or '}'), "unit name").Lexeme();

        internal static readonly Parser<FirstPassUnit> SiiUnit =
            from className in ClassName
            from ns in ClassNameSuffix.Optional()
            from colon in Colon
            from name in UnitName
            from open in OpenCurly
            from pairs in Pair.Many()
            from close in CloseCurly
            select new FirstPassUnit(className + ns.GetOrDefault(), name, pairs.ToList());

        internal static readonly Parser<List<FirstPassUnit>> SiiUnits =
            SiiUnit.Many().Select(x => x.ToList());

        internal static readonly Parser<List<FirstPassUnit>> Sii =
            from header in Parse.String(SiiHeader).Lexeme()
            from oc in OpenCurly
            from u in SiiUnits
            from cc in CloseCurly.Optional()
            select u;

        // Same leading/trailing whitespace policy as Sprache Token(), with no
        // result/list allocation for every whitespace character.
        private static Parser<T> Lexeme<T>(this Parser<T> parser) => input =>
        {
            var parsed = parser(SkipSpace(input));
            return parsed.WasSuccessful
                ? Result.Success(parsed.Value, SkipSpace(parsed.Remainder)) : parsed;
        };

        private static IInput SkipSpace(IInput input)
        {
            while (!input.AtEnd && char.IsWhiteSpace(input.Current)) input = input.Advance();
            return input;
        }

        private static bool IsHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

        // Only a lexical primitive: no knowledge of units, attributes or values.
        // Advance and Result are the framework's own input/result contracts.
        private static Parser<string> Scan(Predicate<char> accepts, string description,
            int minimum = 1, int maximum = int.MaxValue) => input =>
        {
            var rest = input;
            int length = 0;
            while (!rest.AtEnd && length < maximum && accepts(rest.Current))
            {
                rest = rest.Advance();
                length++;
            }
            return length < minimum
                ? Result.Failure<string>(rest, "Expected " + description, new[] { description })
                : Result.Success(input.Source.Substring(input.Position, length), rest);
        };

        private static Number MakeNumber(string text, bool real) => new(real ? null : ParseInt(text, NumberStyles.Integer), float.Parse(text, NumberStyles.Float, culture), true);
        private static float F(TupleValue t, int i) => t.Components[i].Number.Floating;
        private static Vector3 ToVector3(TupleValue t) => new(F(t, 0), F(t, 1), F(t, 2));
        private static Quaternion ToQuaternion(TupleValue t) => new(F(t, 1), F(t, 2), F(t, 3), F(t, 0));
        private static object ToValue(TupleValue t)
        {
            var v = t.Components;
            if (!t.Mat && !t.Semicolon && v.All(c => c.Number.Integer is not null))
                return v.Length switch
                {
                    2 => (v[0].Number.Integer, v[1].Number.Integer),
                    3 => (v[0].Number.Integer, v[1].Number.Integer, v[2].Number.Integer),
                    4 => (v[0].Number.Integer, v[1].Number.Integer, v[2].Number.Integer, v[3].Number.Integer),
                    _ => throw new FormatException("Expected two to four tuple components.")
                };
            if (!t.HasFloat) throw new FormatException("Expected floating-point components.");
            return v.Length switch
            {
                2 => new Vector2(F(t, 0), F(t, 1)), 3 => ToVector3(t),
                4 when t.Mat => new Vector4(F(t, 0), F(t, 1), F(t, 2), F(t, 3)),
                4 => ToQuaternion(t), _ => throw new FormatException("Expected two to four tuple components.")
            };
        }

        internal static readonly Parser<UnitHeader> MatUnitHeader =
            from c in ClassName
            from _ in Colon
            from u in DelimitedString.Select(value => (string)value)
            select new UnitHeader(c, u);

        internal static readonly Parser<KeyValuePair<string, dynamic>> MatUnitValue =
            Parse.Ref(() => MatUnit).Select(x => new KeyValuePair<string, dynamic>(x.ClassName, x));

        internal static readonly Parser<KeyValuePair<string, dynamic>> MatUnitElement =
            MatUnitValue.Or(Pair);

        internal static readonly Parser<FirstPassUnit> MatUnit =
            from h in MatUnitHeader.Token()
            from _ in OpenCurly
            from p in MatUnitElement.Many()
            from __ in CloseCurly.Optional()
            select new FirstPassUnit(h.ClassName, h.UnitName, p.ToList());

        internal static readonly Parser<FirstPassUnit> Mat = MatUnit.Select(x => x);

        internal static object ParseInt(string n, NumberStyles numberStyles = NumberStyles.Integer)
        {
            if (int.TryParse(n, numberStyles, culture, out int intResult))
            {
                return intResult;
            }
            if (long.TryParse(n, numberStyles, culture, out long longResult))
            {
                return longResult;
            }
            if (ulong.TryParse(n, numberStyles, culture, out ulong ulongResult))
            {
                return ulongResult;
            }
            // An integer-looking literal can be a valid float placement component.
            return null;
        }

        internal static void SerializeAttributes(StringBuilder sb, Dictionary<string, dynamic> attributes, 
            string indentation, bool isMat = false)
        {
            foreach (var attrib in attributes)
            {
                if (attrib.Value is Array arr)
                {
                    sb.AppendLine($"{indentation}{attrib.Key}: {arr.Length}");
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (arr.GetValue(i) is null)
                            continue;

                        sb.Append($"{indentation}{attrib.Key}[{i}]: ");
                        SerializeAttributeValue(sb, arr.GetValue(i), isMat);
                        sb.AppendLine();
                    }
                }
                else if (attrib.Value is IList list)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        sb.Append($"{indentation}{attrib.Key}[]: ");
                        SerializeAttributeValue(sb, list[i], isMat);
                        sb.AppendLine();
                    }
                }
                else
                {
                    sb.Append($"{indentation}{attrib.Key}: ");
                    SerializeAttributeValue(sb, attrib.Value, isMat);
                    sb.AppendLine();
                }
            }
        }

        private static void SerializeAttributeValue(StringBuilder sb, dynamic attribValue, bool isMat = false)
        {
            switch (attribValue)
            {
                case string _:
                    sb.Append($"{StringDelimiterChar}{attribValue}{StringDelimiterChar}");
                    break;
                case Array arr:
                    SerializeTuple(sb, arr, isMat);
                    break;
                case double d:
                    // force ".0" if number has no decimals so the game will definitely parse it as float.
                    if (d % 1 == 0)
                        sb.Append(d.ToString("F1", culture));
                    else
                        sb.Append(d.ToString(culture));
                    break;
                case float f:
                    // force ".0" if number has no decimals so the game will definitely parse it as float.
                    if (f % 1 == 0)
                        sb.Append(f.ToString("F1", culture));
                    else
                        sb.Append(f.ToString(culture));
                    break;
                case bool b:
                    sb.Append(b ? True : False);
                    break;
                case Token t:
                    sb.Append(t.String);
                    break;
                case Quaternion q:
                    SerializeQuaternion(sb, q, isMat);
                    break;
                case Vector2 v2:
                    SerializeTuple(sb, new[] { v2.X, v2.Y }, isMat);
                    break;
                case Vector3 v3:
                    SerializeTuple(sb, new[] { v3.X, v3.Y, v3.Z }, isMat);
                    break;
                case Vector4 v4:
                    SerializeTuple(sb, new[] { v4.X, v4.Y, v4.Z, v4.W }, isMat);
                    break;
                case Placement p:
                    SerializeAttributeValue(sb, p.Position);
                    sb.Append(' ');
                    SerializeAttributeValue(sb, p.Rotation);
                    break;
                default:
                    sb.Append(Convert.ToString(attribValue, culture));
                    break;
            }
        }

        private static void SerializeQuaternion(StringBuilder sb, Quaternion q, bool isMat = false)
        {
            sb.Append(isMat ? MatTupleOpenChar : TupleOpenChar);
            SerializeAttributeValue(sb, q.W);
            sb.Append($"{QuaternionWSeparatorChar} ");
            SerializeAttributeValue(sb, q.X);
            sb.Append($"{TupleSeparatorChar} ");
            SerializeAttributeValue(sb, q.Y);
            sb.Append($"{TupleSeparatorChar} ");
            SerializeAttributeValue(sb, q.Z);
            sb.Append(isMat ? MatTupleCloseChar : TupleCloseChar);
        }

        private static void SerializeTuple(StringBuilder sb, IList arr, bool isMat = false)
        {
            sb.Append(isMat ? MatTupleOpenChar : TupleOpenChar);
            for (int i = 0; i < arr.Count; i++)
            {
                SerializeAttributeValue(sb, arr[i]);
                if (i != arr.Count - 1)
                    sb.Append($"{TupleSeparatorChar} ");
            }
            sb.Append(isMat ? MatTupleCloseChar : TupleCloseChar);
        }
    }

    internal record UnitHeader(string ClassName, string UnitName);

    internal record FirstPassUnit(string ClassName, string UnitName, 
        List<KeyValuePair<string, dynamic>> Attributes);
}
