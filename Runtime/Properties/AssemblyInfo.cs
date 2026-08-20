using System.Runtime.CompilerServices;

[assembly: UniMob.AtomGenerateDebugNames]
// Zone is the frame clock and is internal, so the fixture that proves the real driver actually
// drives -- the one thing a fake clock cannot prove about itself -- has to be a friend.
[assembly: InternalsVisibleTo("UniMob.UI.Tests.PlayMode")]
