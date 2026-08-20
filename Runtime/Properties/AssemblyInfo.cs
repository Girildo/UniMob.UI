using System.Runtime.CompilerServices;

[assembly: UniMob.AtomGenerateDebugNames]
// The fake clock derives from Zone, whose constructor is internal, and installs itself through an
// internal slot -- a closed hierarchy is the point, so the fake is a friend rather than the door
// being opened to everyone.
[assembly: InternalsVisibleTo("UniMob.UI.Testing")]
// Zone is internal, so the fixture that proves the real driver actually drives -- the one thing a
// fake clock cannot prove about itself -- has to be a friend too.
[assembly: InternalsVisibleTo("UniMob.UI.Tests.PlayMode")]
// The package's own EditMode fixtures drive the internal clock directly.
[assembly: InternalsVisibleTo("UniMob.UI.Tests")]
