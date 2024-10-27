using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace TruckLib.Sii
{
    /// <summary>
    /// Represents an SII file.
    /// </summary>
    public class SiiFile
    {
        // https://modding.scssoft.com/wiki/Documentation/Engine/Units

        /// <summary>
        /// Units in this file.
        /// </summary>
        public List<Unit> Units { get; set; } = [];

        /// <summary>
        /// Instantiates an empty SII file.
        /// </summary>
        public SiiFile() { }

        /// <summary>
        /// Deserializes a SII file.
        /// </summary>
        /// <param name="sii">The string containing the SII file.</param>
        /// <param name="siiDirectory">The path of the directory in which the SII file is located.
        /// Required for inserting <c>@include</c>s. Can be omitted if the file is known not to
        /// have <c>@include</c>s.</param>
        /// <returns>A SiiFile object.</returns>
        public static SiiFile Load(string sii, string siiDirectory = "") =>
            Load(sii, siiDirectory, new DiskFileSystem());

        /// <summary>
        /// Deserializes a SII file.
        /// </summary>
        /// <param name="sii">The string containing the SII file.</param>
        /// <param name="siiDirectory">The path of the directory in which the SII file is located.
        /// Required for inserting <c>@include</c>s. Can be omitted if the file is known not to
        /// have <c>@include</c>s.</param>
        /// <param name="fs">The file system to load <c>@include</c>d files from.</param>
        /// <returns>A <see>SiiFile</see> object.</returns>
        public static SiiFile Load(string sii, string siiDirectory, IFileSystem fs) =>
            SiiParser.DeserializeFromString(sii, siiDirectory, fs);


        /// <summary>
        /// Deserializes a SII file.
        /// </summary>
        /// <param name="sii">The buffer containing the SII file.</param>
        /// <param name="siiDirectory">The path of the directory in which the SII file is located.
        /// Required for inserting <c>@include</c>s. Can be omitted if the file is known not to
        /// have <c>@include</c>s.</param>
        /// <returns>A <see>SiiFile</see> object.</returns>
        public static SiiFile Load(byte[] sii, string siiDirectory = "") =>
            Load(sii, siiDirectory, new DiskFileSystem());

        /// <summary>
        /// Deserializes a SII file.
        /// </summary>
        /// <param name="sii">The buffer containing the SII file.</param>
        /// <param name="siiDirectory">The path of the directory in which the SII file is located.
        /// Required for inserting <c>@include</c>s. Can be omitted if the file is known not to
        /// have <c>@include</c>s.</param>
        /// <param name="fs">The file system to load <c>@include</c>d files from.</param>
        /// <returns>A <see>SiiFile</see> object.</returns>
        public static SiiFile Load(byte[] sii, string siiDirectory, IFileSystem fs)
        {
            var magic = Encoding.ASCII.GetString(sii[0..4]);
            if (magic == "ScsC")
            {
                var decrypted = EncryptedSii.Decrypt(sii);
                return Load(decrypted, siiDirectory, fs);
            }
            else
            {
                return SiiParser.DeserializeFromString(Encoding.UTF8.GetString(sii), siiDirectory, fs);
            }
        }

        /// <summary>
        /// Opens a SII file.
        /// </summary>
        /// <param name="path">The path of the file.</param>
        /// <returns>A SiiFile object.</returns>
        public static SiiFile Open(string path) =>
            Open(path, new DiskFileSystem());

        /// <summary>
        /// Opens a SII file.
        /// </summary>
        /// <param name="path">The path of the file.</param>
        /// <param name="fs">The file system to load this file and <c>@include</c>d files from.</param>
        /// <returns>A <see>SiiFile</see> object.</returns>
        public static SiiFile Open(string path, IFileSystem fs)
        {
            var file = fs.ReadAllBytes(path);
            var siiDirectory = fs.GetParent(path);
            return Load(file, siiDirectory, fs);
        }
            
        /// <summary>
        /// Serializes this object to a string.
        /// </summary>
        /// <param name="indentation">The string used as indentation inside units.</param>
        public string Serialize(string indentation = "\t") =>
            SiiParser.Serialize(this, indentation);

        /// <summary>
        /// Serializes this object and writes it to a file.
        /// </summary>
        /// <param name="path">The output path.</param>
        /// <param name="indentation">The string used as indentation inside units.</param>
        public void Save(string path, string indentation = "\t") =>
            SiiParser.Save(this, path, indentation);
    }
}
