using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace WhalePet
{
    internal sealed class SatietyMeter
    {
        internal const long IntervalMs = 300000;
        internal int Value { get; private set; }
        internal long ElapsedMs { get; private set; }
        private long lastMs;

        internal SatietyMeter(int value, long elapsedMs, long nowMs)
        {
            Value = Math.Max(0, Math.Min(100, value));
            ElapsedMs = Math.Max(0, Math.Min(IntervalMs - 1, elapsedMs));
            lastMs = nowMs;
        }

        internal void Advance(long nowMs)
        {
            long delta = Math.Max(0, nowMs - lastMs);
            lastMs = nowMs;
            long total = ElapsedMs + delta;
            Value = (int)Math.Max(0, Value - total / IntervalMs);
            ElapsedMs = total % IntervalMs;
        }

        internal int Feed(int successfulFiles)
        {
            int before = Value;
            Value = (int)Math.Min(100L, Value + Math.Max(0L, successfulFiles) * 10L);
            return Value - before;
        }
    }

    internal static class AwakeClock
    {
        // Unlike TickCount/Stopwatch, Windows unbiased interrupt time excludes sleep/hibernation.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryUnbiasedInterruptTime(out ulong time);
        internal static long Milliseconds
        {
            get
            {
                ulong time;
                if (!QueryUnbiasedInterruptTime(out time))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                return (long)(time / 10000);
            }
        }
    }

    internal sealed class FeedingResult
    {
        internal int Successful;
        internal int Failed;
        internal readonly List<string> Errors = new List<string>();
    }

    internal static class FileFeeding
    {
        internal static string[] Normalize(string[] paths)
        {
            List<string> items = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (paths == null) return items.ToArray();
            foreach (string path in paths)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) continue;
                    string full = Path.GetFullPath(path);
                    if (seen.Add(full)) items.Add(full);
                }
                catch (Exception error) { Console.Error.WriteLine("[pet] invalid feed path: " + error.Message); }
            }
            return items.ToArray();
        }

        internal static bool IsRegularLocalFile(string path)
        {
            try
            {
                // UNC, device paths, directories and reparse points are not ordinary local files.
                if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
                FileAttributes attributes = File.GetAttributes(path);
                return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
            }
            catch { return false; }
        }

        internal static FeedingResult Process(string[] paths, Func<string, bool> recycle)
        {
            FeedingResult result = new FeedingResult();
            foreach (string path in Normalize(paths))
            {
                try
                {
                    if (!IsRegularLocalFile(path)) throw new IOException("仅接受存在的本地普通文件");
                    if (!recycle(path)) throw new IOException("文件未进入回收站");
                    result.Successful++;
                }
                catch (Exception error)
                {
                    result.Failed++;
                    result.Errors.Add(Path.GetFileName(path) + "：" + error.Message);
                }
            }
            return result;
        }
    }

    public static class RecycleBin
    {
        // RECYCLEONDELETE + ADDUNDORECORD + EARLYFAILURE, with ALLOWUNDO for Shell compatibility.
        // Never enable elevation or fall back to File.Delete/SHFileOperation.
        private const uint RecycleFlags = 0x00080000 | 0x20000000 | 0x00100000
            | 0x0400 | 0x0010 | 0x0004 | 0x0200 | 0x0040 | 0x2000;
        private static readonly Guid ShellItemId = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr bind, ref Guid id,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        internal static bool Recycle(string path)
        {
            if (!FileFeeding.IsRegularLocalFile(path)) return false;
            IFileOperation operation = null;
            IShellItem item = null;
            try
            {
                operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(
                    new Guid("3AD05575-8857-4850-9277-11B85BDB8E09")));
                operation.SetOperationFlags(RecycleFlags);
                Guid id = ShellItemId;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref id, out item);
                RecycleSink sink = new RecycleSink();
                operation.DeleteItem(item, sink);
                operation.PerformOperations();
                bool aborted;
                operation.GetAnyOperationsAborted(out aborted);
                return !aborted && sink.Recycled;
            }
            finally
            {
                if (item != null) Marshal.ReleaseComObject(item);
                if (operation != null) Marshal.ReleaseComObject(operation);
            }
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItem
        {
            void BindToHandler(IntPtr bind, ref Guid handler, ref Guid id, out IntPtr value);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint name, out IntPtr value);
            void GetAttributes(uint mask, out uint value);
            void Compare(IShellItem other, uint hint, out int order);
        }

        // Keep the complete native vtable order, including unused operations.
        [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOperation
        {
            void Advise(IFileOperationProgressSink sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOperationFlags(uint flags);
            void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
            void SetProgressDialog(IntPtr dialog);
            void SetProperties(IntPtr properties);
            void SetOwnerWindow(IntPtr window);
            void ApplyPropertiesToItem(IShellItem item);
            void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
            void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
            void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string name);
            void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
            void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
            void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink sink);
            void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
            void DeleteItem(IShellItem item, IFileOperationProgressSink sink);
            void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
            void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name,
                [MarshalAs(UnmanagedType.LPWStr)] string template, IFileOperationProgressSink sink);
            void PerformOperations();
            void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
        }

        [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFileOperationProgressSink
        {
            [PreserveSig] int StartOperations();
            [PreserveSig] int FinishOperations(int result);
            [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem created);
            [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem created);
            [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem created);
            [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
            [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem created);
            [PreserveSig] int PreNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
            [PreserveSig] int PostNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name,
                [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int result, IShellItem created);
            [PreserveSig] int UpdateProgress(uint total, uint completed);
            [PreserveSig] int ResetTimer();
            [PreserveSig] int PauseTimer();
            [PreserveSig] int ResumeTimer();
        }

        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        public sealed class RecycleSink : IFileOperationProgressSink
        {
            internal bool Recycled;
            public int PreDeleteItem(uint flags, IShellItem item)
            {
                // Veto any operation which the Shell no longer intends to recycle.
                return (flags & 0x80) != 0 ? 0 : unchecked((int)0x80004004);
            }
            public int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem created)
            {
                // A successful HRESULT alone is insufficient: created is the item in the bin.
                Recycled = result >= 0 && created != null;
                return 0;
            }
            public int StartOperations() { return 0; }
            public int FinishOperations(int result) { return 0; }
            public int PreRenameItem(uint f, IShellItem i, string n) { return 0; }
            public int PostRenameItem(uint f, IShellItem i, string n, int r, IShellItem c) { return 0; }
            public int PreMoveItem(uint f, IShellItem i, IShellItem d, string n) { return 0; }
            public int PostMoveItem(uint f, IShellItem i, IShellItem d, string n, int r, IShellItem c) { return 0; }
            public int PreCopyItem(uint f, IShellItem i, IShellItem d, string n) { return 0; }
            public int PostCopyItem(uint f, IShellItem i, IShellItem d, string n, int r, IShellItem c) { return 0; }
            public int PreNewItem(uint f, IShellItem d, string n) { return 0; }
            public int PostNewItem(uint f, IShellItem d, string n, string t, uint a, int r, IShellItem c) { return 0; }
            public int UpdateProgress(uint t, uint c) { return 0; }
            public int ResetTimer() { return 0; }
            public int PauseTimer() { return 0; }
            public int ResumeTimer() { return 0; }
        }
    }
}
