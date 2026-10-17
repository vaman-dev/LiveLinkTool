using NUnit.Framework;
using UnityEngine;

namespace LiveLink.Tests
{
    public class LiveLinkLightTests
    {
        [Test]
        public void LightState_ValidatesSupportedValues()
        {
            var state = new LiveLinkLightState
            {
                enabled = true,
                intensity = 3.5f,
                color = Color.red,
                range = 12f,
                spotAngle = 30f
            };

            Assert.That(
                LiveLinkLightStateValidator.TryValidate(
                    state, out string error), Is.True, error);
        }

        [Test]
        public void LightState_RejectsInvalidRange()
        {
            var state = new LiveLinkLightState
            {
                intensity = 1f,
                color = Color.white,
                range = 0f,
                spotAngle = 30f
            };

            Assert.That(
                LiveLinkLightStateValidator.TryValidate(
                    state, out string error), Is.False);
            Assert.That(error, Does.Contain("range"));
        }

        [Test]
        public void LightState_RejectsInvalidSpotAngle()
        {
            var state = new LiveLinkLightState
            {
                intensity = 1f,
                color = Color.white,
                range = 10f,
                spotAngle = 180f
            };

            Assert.That(
                LiveLinkLightStateValidator.TryValidate(
                    state, out string error), Is.False);
            Assert.That(error, Does.Contain("Spot angle"));
        }

        [Test]
        public void LightState_RoundTripsThroughJson()
        {
            var state = new LiveLinkLightState
            {
                enabled = false,
                intensity = 2.25f,
                color = new Color(1f, 0.2f, 0.1f, 1f),
                range = 8f,
                spotAngle = 45f
            };

            string json = JsonUtility.ToJson(state);
            LiveLinkLightState restored =
                JsonUtility.FromJson<LiveLinkLightState>(json);

            Assert.That(restored.enabled, Is.False);
            Assert.That(restored.intensity, Is.EqualTo(2.25f));
            Assert.That(restored.range, Is.EqualTo(8f));
            Assert.That(restored.spotAngle, Is.EqualTo(45f));
            Assert.That(restored.color.r, Is.EqualTo(1f));
            Assert.That(restored.color.g, Is.EqualTo(0.2f));
        }
    }
}
