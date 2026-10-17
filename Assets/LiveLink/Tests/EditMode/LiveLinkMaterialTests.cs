using NUnit.Framework;
using UnityEngine;

namespace LiveLink.Tests
{
    public class LiveLinkMaterialTests
    {
        [Test]
        public void MaterialState_AcceptsBaseColor()
        {
            Assert.That(
                LiveLinkMaterialStateValidator.TryValidate(
                    "BaseColor",
                    new LiveLinkMaterialState
                    {
                        color = Color.blue
                    },
                    out string error), Is.True, error);
        }

        [Test]
        public void MaterialState_AcceptsNumericProperties()
        {
            Assert.That(
                LiveLinkMaterialStateValidator.TryValidate(
                    "Metallic",
                    new LiveLinkMaterialState { value = 1f },
                    out string metallicError), Is.True, metallicError);
            Assert.That(
                LiveLinkMaterialStateValidator.TryValidate(
                    "Smoothness",
                    new LiveLinkMaterialState { value = 0.5f },
                    out string smoothnessError), Is.True, smoothnessError);
        }

        [Test]
        public void MaterialState_RejectsUnsupportedProperty()
        {
            Assert.That(
                LiveLinkMaterialStateValidator.TryValidate(
                    "Texture",
                    new LiveLinkMaterialState(),
                    out string error), Is.False);
            Assert.That(error, Does.Contain("not supported"));
        }

        [Test]
        public void MaterialState_RejectsOutOfRangeNumericValue()
        {
            Assert.That(
                LiveLinkMaterialStateValidator.TryValidate(
                    "Metallic",
                    new LiveLinkMaterialState { value = 2f },
                    out string error), Is.False);
            Assert.That(error, Does.Contain("between 0 and 1"));
        }

        [Test]
        public void MaterialCommand_RoundTripsThroughJson()
        {
            var command = new LiveLinkCommand
            {
                type = "MATERIAL_UPDATE",
                objectId = "material-object",
                rendererComponentIndex = 0,
                materialSlot = 1,
                materialProperty = "BaseColor",
                material = new LiveLinkMaterialState
                {
                    color = new Color(0.1f, 0.2f, 0.8f, 1f)
                }
            };

            LiveLinkCommand restored = JsonUtility.FromJson<LiveLinkCommand>(
                JsonUtility.ToJson(command));

            Assert.That(restored.type, Is.EqualTo("MATERIAL_UPDATE"));
            Assert.That(restored.materialSlot, Is.EqualTo(1));
            Assert.That(restored.materialProperty, Is.EqualTo("BaseColor"));
            Assert.That(restored.material.color.b, Is.EqualTo(0.8f));
        }
    }
}
