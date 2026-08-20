// IsExternalInit is what the compiler binds `init` accessors against, and Unity's netstandard2.1
// reference assemblies do not carry it. Internal, because the consuming project declares its own
// copy and two public ones in scope are CS0433.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
