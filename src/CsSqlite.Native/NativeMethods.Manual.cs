using System;
using System.Runtime.InteropServices;

namespace CsSqlite
{
    // IL2CPP (Unity/Android) cannot marshal function-pointer (`delegate* unmanaged[Cdecl]`)
    // parameters in DllImport signatures and throws
    // "MarshalDirectiveException: Cannot marshal type 'System.IntPtr'" at call time.
    // These overloads bind the same native entry points with the destructor parameter
    // declared as IntPtr (SQLITE_STATIC = 0, SQLITE_TRANSIENT = -1), which marshals
    // correctly on every runtime. Use these instead of the generated fnptr variants.
    public static unsafe partial class NativeMethods
    {
        [DllImport(__DllName, EntryPoint = "sqlite3_bind_blob", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int sqlite3_bind_blob_intptr(sqlite3_stmt* stmt, int index, void* value, int n, IntPtr destructor);

        [DllImport(__DllName, EntryPoint = "sqlite3_bind_text", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int sqlite3_bind_text_intptr(sqlite3_stmt* stmt, int index, byte* value, int n, IntPtr destructor);

        [DllImport(__DllName, EntryPoint = "sqlite3_bind_text16", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int sqlite3_bind_text16_intptr(sqlite3_stmt* stmt, int index, void* value, int n, IntPtr destructor);
    }
}
