using Sprache;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("TruckLib.Sii.Tests")]
namespace TruckLib.Sii
{
    internal static class MatParser
    {
        public static MatFile DeserializeFromString(string mat)
        {
            mat = Utils.TrimByteOrderMark(mat);

            var firstPass = ParserElements.Mat.Parse(mat);

            var matFile = new MatFile { Effect = firstPass.UnitName };

            var (secondPass, textures) = SecondPass(firstPass);
            matFile.Attributes = secondPass.Attributes;
            matFile.Textures = textures;
            return matFile;
        }

        private static (Unit, List<Texture>) SecondPass(FirstPassUnit firstPass)
        {
            Dictionary<string, int> arrInsertIndex = [];
            List<Texture> textures = [];

            var secondPass = new Unit(firstPass.ClassName, firstPass.UnitName);
            foreach (var (key, value) in firstPass.Attributes)
            {
                if (key.EndsWith(']'))
                {
                    SiiMatUtils.ParseListOrArrayAttribute(secondPass, key, value, 
                        arrInsertIndex, false);
                }
                else
                {
                    if (key == "texture" && value is FirstPassUnit fp)
                    {
                        var (sp, _) = SecondPass(fp);
                        textures.Add(new Texture()
                        {
                            Name = sp.Name,
                            Attributes = sp.Attributes,
                        });
                        secondPass.Attributes.Remove("texture");
                    } 
                    else
                    {
                        SiiMatUtils.AddAttribute(secondPass, key, value, false);
                    }
                }
            }

            // convert legacy mat
            if (secondPass.Attributes.ContainsKey("texture") 
                && secondPass.Attributes.ContainsKey("texture_name"))
            {
                var legacyTextures = secondPass.Attributes["texture"];
                var legacyTextureNames = secondPass.Attributes["texture_name"];
                if (legacyTextures is string)
                {
                    var texture = new Texture();
                    texture.Name = legacyTextureNames;
                    texture.Attributes.Add("source", legacyTextures);
                    textures.Add(texture);
                }
                else if (legacyTextures is object[])
                {
                    for (int i = 0; i < legacyTextures.Length; i++)
                    {
                        var texture = new Texture();
                        texture.Name = legacyTextureNames[i];
                        texture.Attributes.Add("source", legacyTextures[i]);
                        textures.Add(texture);
                    }
                }
                else
                {
                    throw new NotSupportedException();
                }
                secondPass.Attributes.Remove("texture");
                secondPass.Attributes.Remove("texture_name");
            }

            return (secondPass, textures);
        }

        public static MatFile DeserializeFromFile(string path, IFileSystem fs) =>
            DeserializeFromString(fs.ReadAllText(path));

        public static string Serialize(MatFile matFile, string indentation = "\t")
        {
            var sb = new StringBuilder();

            sb.AppendLine($"effect : \"{matFile.Effect}\" {{");

            ParserElements.SerializeAttributes(sb, matFile.Attributes, indentation, true);
            foreach (var texture in matFile.Textures)
            {
                sb.AppendLine($"{indentation}texture: \"{texture.Name}\" {{");
                ParserElements.SerializeAttributes(sb, texture.Attributes, 
                    indentation + indentation, true);
                sb.AppendLine($"{indentation}}}");
            }

            sb.AppendLine("}\n");

            return sb.ToString();
        }

        public static void Serialize(MatFile matFile, string path, string indentation = "\t")
        {
            var str = Serialize(matFile, indentation);
            File.WriteAllText(path, str);
        }
    } 
}
