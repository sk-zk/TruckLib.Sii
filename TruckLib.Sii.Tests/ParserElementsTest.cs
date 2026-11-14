using Sprache;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace TruckLib.Sii.Tests;

public class ParserElementsTest
{
    // Expected types and values from unmodified upstream, written explicitly so
    // regressions remain reviewable without another parser implementation.
    public static TheoryData<string, object> Values => new()
    {
        { "0", 0 },
        { "-0", 0 },
        { "+42", 42 },
        { "2147483648", 2147483648L },
        { "-2147483649", -2147483649L },
        { "9223372036854775808", 9223372036854775808UL },
        { "18446744073709551615", 18446744073709551615UL },
        { "0xBEEF", 48879 },
        { "0xffffffff", -1 },
        { "0x100000000", 4294967296L },
        { "0xffffffffffffffff", -1L },
        { "1.0", 1f },
        { "-0.0", BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)) },
        { "727.", 727f },
        { "4.2f", 4.2f },
        { "1e3", 1000f },
        { "1.2e-3", 0.0012f },
        { "1e9999", float.PositiveInfinity },
        { "&00000000", 0f },
        { "&80000000", BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)) },
        { "&7fc01234", BitConverter.Int32BitsToSingle(unchecked((int)0x7FC01234)) },
        { "&7f800000", float.PositiveInfinity },
        { "true", true },
        { "false", false },
        { "default", new Token("default") },
        { "twelve_chars", new Token("twelve_chars") },
        { "thirteen_char", new LinkPointer("thirteen_char") },
        { "name.reference", new LinkPointer("name.reference") },
        { ".42owner", new OwnerPointer(".42owner") },
        { "1wcy1", new Token("1wcy1") },
        { "1409fshw0", new Token("1409fshw0") },
        { "350m", new Token("350m") },
        { "4200v", new Token("4200v") },
        { "123abc", new Token("123abc") },
        { "1e", new Token("1e") },
        { "1e+", new LinkPointer("1e+") },
        { "-abc", new LinkPointer("-abc") },
        { "+abc", new LinkPointer("+abc") },
        { "1.2foo", new LinkPointer("1.2foo") },
        { "\"hello world\"", "hello world" },
        { "\"\"", "" },
        { "\"  trimmed  \"", "trimmed" },
        { "\"Unicode: München 城区\"", "Unicode: München 城区" },
        { "(1, 2)", ((object)1, (object)2) },
        { "(1, 2, 3)", ((object)1, (object)2, (object)3) },
        { "(1, 2, 3, 4)", ((object)1, (object)2, (object)3, (object)4) },
        { "(1, 2147483648, 18446744073709551615)", ((object)1, (object)2147483648L, (object)18446744073709551615UL) },
        { "(0xffffffff, 0x100000000)", ((object)-1, (object)4294967296L) },
        { "(1.0, 2)", new Vector2(1f, 2f) },
        { "(1, 2.0, 3)", new Vector3(1f, 2f, 3f) },
        { "(1.0, 2, 3, 4)", new Quaternion(2f, 3f, 4f, 1f) },
        { "(4; 1, 2, 3)", new Quaternion(1f, 2f, 3f, 4f) },
        { "(&80000000, &7fc01234, 1)", new Vector3(BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)), BitConverter.Int32BitsToSingle(unchecked((int)0x7FC01234)), 1f) },
        { "(1, 2, 3) (4; 5, 6, 7)", new Placement(new Vector3(1f, 2f, 3f), new Quaternion(5f, 6f, 7f, 4f)) },
        { "{1, 2}", new Vector2(1f, 2f) },
        { "{1, 2, 3}", new Vector3(1f, 2f, 3f) },
        { "{1, 2, 3, 4}", new Vector4(1f, 2f, 3f, 4f) },
        { "(18446744073709551616, 2, 3) (1; 0, 0, 0)", new Placement(new Vector3(1.8446744E+19f, 2f, 3f), new Quaternion(0f, 0f, 0f, 1f)) },
        { "{18446744073709551616, 1}", new Vector2(1.8446744E+19f, 1f) },
        { "(1 2 3)", ((object)1, (object)2, (object)3) },
        { "(1,2,3,)", ((object)1, (object)2, (object)3) },
        { "(1-2)", ((object)1, (object)-2) },
        { "(1f,2f)", new Vector2(1f, 2f) },
        { "(-0.24, 0 , -0.97)", new Vector3(-0.24f, 0f, -0.97f) },
        { "(1 , 2 , 3)", ((object)1, (object)2, (object)3) },
        { "(4 ; 1 , 2 , 3)", new Quaternion(1f, 2f, 3f, 4f) },
        { "{1 , 2}", new Vector2(1f, 2f) },
    };

    [Theory, MemberData(nameof(Values))]
    public void PreservesExactValues(string value, object expected)
    {
        string source = "SiiNunit {\n unit:.test {\n value: " + value + "\n }\n}\n";
        var actual = Assert.Single(SiiFile.Load(source).Units).Attributes["value"];
        EqualValue(expected, (object)actual);
    }

    [Theory]
    [InlineData("+ 42", 42)]
    [InlineData("-\t42", -42)]
    public void SharedPrefixesAndSpacedSigns(string value, int expected)
    {
        string source = "SiiNunit {\n ui::window:.test {\n item [ 3 ]: " + value + "\n }\n}\n";
        var unit = Assert.Single(ParserElements.Sii.Parse(source));
        Assert.Equal("ui::window", unit.ClassName);
        Assert.Equal(".test", unit.UnitName);
        Assert.Equal("item[3]", unit.Attributes[0].Key);
        EqualValue(expected, (object)unit.Attributes[0].Value);
    }

    [Fact]
    public void GeneratedDocumentsCoverRepeatedValuesAndUnitBoundaries()
    {
        var random = new Random(4127);
        var rows = Values.Cast<object[]>().ToArray();
        for (int iteration = 0; iteration < 200; iteration++)
        {
            var text = new System.Text.StringBuilder("SiiNunit {\n");
            var expected = new List<FirstPassUnit>();
            for (int unit = 0; unit < random.Next(1, 5); unit++)
            {
                text.Append("kind : .unit").Append(unit).Append(" {\n");
                var attributes = new List<KeyValuePair<string, dynamic>>();
                for (int item = 0; item < random.Next(1, 12); item++)
                {
                    var row = rows[random.Next(rows.Length)];
                    string key = "value" + item + "[]";
                    text.Append(key).Append(": ").Append((string)row[0]).Append('\n');
                    attributes.Add(new(key, row[1]));
                }
                text.Append("}\n");
                expected.Add(new("kind", ".unit" + unit, attributes));
            }
            text.Append('}');
            Equal(expected, ParserElements.Sii.Parse(text.ToString()));
        }
    }

    private static void Equal(List<FirstPassUnit> expected, List<FirstPassUnit> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].ClassName, actual[i].ClassName);
            Assert.Equal(expected[i].UnitName, actual[i].UnitName);
            Assert.Equal(expected[i].Attributes.Count, actual[i].Attributes.Count);
            for (int j = 0; j < expected[i].Attributes.Count; j++)
            {
                Assert.Equal(expected[i].Attributes[j].Key, actual[i].Attributes[j].Key);
                EqualValue((object)expected[i].Attributes[j].Value, (object)actual[i].Attributes[j].Value);
            }
        }
    }

    private static void EqualValue(object expected, object actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        if (expected is float number) Assert.Equal(BitConverter.SingleToInt32Bits(number), BitConverter.SingleToInt32Bits((float)actual));
        else if (expected is ITuple tuple)
            for (int i = 0; i < tuple.Length; i++) EqualValue(tuple[i]!, ((ITuple)actual)[i]!);
        else if (expected is Vector2 v2)
        { EqualValue(v2.X, ((Vector2)actual).X); EqualValue(v2.Y, ((Vector2)actual).Y); }
        else if (expected is Vector3 v3)
        { EqualValue(v3.X, ((Vector3)actual).X); EqualValue(v3.Y, ((Vector3)actual).Y); EqualValue(v3.Z, ((Vector3)actual).Z); }
        else if (expected is Quaternion q)
        { EqualValue(q.X, ((Quaternion)actual).X); EqualValue(q.Y, ((Quaternion)actual).Y); EqualValue(q.Z, ((Quaternion)actual).Z); EqualValue(q.W, ((Quaternion)actual).W); }
        else if (expected is Placement p)
        { EqualValue(p.Position, ((Placement)actual).Position); EqualValue(p.Rotation, ((Placement)actual).Rotation); }
        else Assert.Equal(expected, actual);
    }
}
