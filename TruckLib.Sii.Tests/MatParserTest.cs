using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using TruckLib.Sii;

namespace TruckLib.Sii.Tests
{
    public class MatParserTest
    {
        [Fact]
        public void DeserializeMat()
        {
            var str = File.ReadAllText("Data/mat_current.mat");
            var mat = MatParser.DeserializeFromString(str);

            Assert.Equal("eut2.dif.spec.mult.dif.iamod.dif.add.env.tsnmap.rfx", mat.Effect);
            Assert.Equal(new Vector2(0.2f, 0.9f), mat.Attributes["fresnel"]);
            Assert.Equal(25f, mat.Attributes["shininess"]);
            Assert.Equal("texture_reflection", mat.Textures[4].Name);
            Assert.Equal("/material/environment/building_reflection/building_ref.tobj", 
                mat.Textures[4].Attributes["source"]);
            Assert.Equal("clamp", mat.Textures[4].Attributes["u_address"]);
        }

        [Fact]
        public void DeserializeLegacyMat()
        {
            var str = File.ReadAllText("Data/mat_legacy.mat");
            var mat = MatParser.DeserializeFromString(str);

            Assert.Equal("eut2.lamp.add.env", mat.Effect);

            Assert.Equal("texture_reflection", mat.Textures[2].Name);
            Assert.Equal("/material/environment/vehicle_reflection.tobj", 
                mat.Textures[2].Attributes["source"]);
        }
    }
}
