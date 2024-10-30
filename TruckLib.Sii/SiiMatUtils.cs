using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TruckLib.Sii
{
    internal class SiiMatUtils
    {
        internal static void AddAttribute(Unit unit, string name, dynamic value, bool overrideOnDuplicate)
        {
            if (unit.Attributes.ContainsKey(name) && overrideOnDuplicate)
            {
                unit.Attributes[name] = value;
            }
            else
            {
                unit.Attributes.Add(name, value);
            }
        }

        internal static void ParseListOrArrayAttribute(Unit unit, string name, dynamic value,
            Dictionary<string, int> arrInsertIndex, bool overrideOnDuplicate)
        {
            var match = Regex.Match(name, @"^(.+)\[(.*)\]$");
            if (!match.Success)
                throw new ArgumentException("Not an array entry attribute", nameof(name));

            var arrName = match.Groups[1].Value;
            var hasArrIndex = int.TryParse(match.Groups[2].Value, out int arrIndex);
            arrInsertIndex.TryAdd(arrName, 0);

            // figure out if this is a fixed-length array entry or a list entry
            // and create the thing if it doesn't exist yet
            bool isFixedLengthArray;
            if (unit.Attributes.TryGetValue(arrName, out var whatsAllThisThen))
            {
                isFixedLengthArray = whatsAllThisThen is int or Array;
            }
            else
            {
                isFixedLengthArray = hasArrIndex;
                if (isFixedLengthArray)
                {
                    int initSize = arrIndex + 1;
                    var arr = new dynamic[initSize];
                    AddAttribute(unit, arrName, arr, overrideOnDuplicate);
                }
                else
                {
                    AddAttribute(unit, arrName, new List<dynamic>(), overrideOnDuplicate);
                }
            }

            // insert the value
            if (isFixedLengthArray)
            {
                var val = unit.Attributes[arrName];
                dynamic[] arr;
                if (val is int)
                {
                    // existing val is int => it's a fixed-length array
                    // where the length has been read in, and now we need to
                    // create the actual array
                    arr = new dynamic[val];
                    unit.Attributes[arrName] = arr;
                }
                else
                {
                    arr = val;
                }

                if (arr.Length < arrIndex + 1)
                {
                    Array.Resize(ref arr, arrIndex + 1);
                    unit.Attributes[arrName] = arr;
                }

                if (hasArrIndex)
                {
                    arrInsertIndex[arrName] = arrIndex + 1;
                }
                else
                {
                    arrIndex = arrInsertIndex[arrName]++;
                }
                if (arr.Length <= arrIndex)
                {
                    MiscExtensions.Push(arr, value);
                } 
                else
                {
                    arr[arrIndex] = value;
                }
            }
            else
            {
                unit.Attributes[arrName].Add(value);
            }
        }
    }
}
