using LiveLink;
using NUnit.Framework;
using UnityEngine;

namespace LiveLink.Tests
{
    public sealed class LiveLinkSmokeTests
    {
        [Test]
        public void LiveLinkIdentity_CanBeAttached()
        {
            var gameObject =
                new GameObject("LiveLink_Test_Object");

            try
            {
                var identity =
                    gameObject.AddComponent<LiveLinkIdentity>();

                Assert.That(identity, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void TwoComponents_AreDifferentInstances()
        {
            var objectA = new GameObject("Test_A");
            var objectB = new GameObject("Test_B");

            try
            {
                var identityA =
                    objectA.AddComponent<LiveLinkIdentity>();

                var identityB =
                    objectB.AddComponent<LiveLinkIdentity>();

                Assert.That(identityA, Is.Not.SameAs(identityB));
            }
            finally
            {
                Object.DestroyImmediate(objectA);
                Object.DestroyImmediate(objectB);
            }
        }
    }
}
