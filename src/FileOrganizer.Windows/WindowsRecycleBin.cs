using System.Runtime.InteropServices;
using FileOrganizer.Core;

namespace FileOrganizer.Windows;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsRecycleBin : IRecycleBin
{
    public Task RecycleAsync(string path, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            IFileOperation? operation = null;
            IShellItem? item = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var type = Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), throwOnError: true)!;
                operation = (IFileOperation)Activator.CreateInstance(type)!;
                var iid = typeof(IShellItem).GUID;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item));
                // RECYCLEONDELETE를 지정한다. 실패 시 영구 삭제로 재시도하지 않는다.
                const uint flags = 0x0004 | 0x0010 | 0x0040 | 0x0400 | 0x00080000 | 0x00100000;
                Marshal.ThrowExceptionForHR(operation.SetOperationFlags(flags));
                var sink = new RecycleOnlySink();
                Marshal.ThrowExceptionForHR(operation.DeleteItem(item, sink));
                var result = operation.PerformOperations();
                GC.KeepAlive(sink);
                if (sink.RefusedPermanentDelete)
                    throw new IOException("이 파일은 휴지통으로 옮길 수 없어 교체를 중단했습니다. 기존 파일은 보존했습니다.");
                Marshal.ThrowExceptionForHR(result);
                Marshal.ThrowExceptionForHR(operation.GetAnyOperationsAborted(out var aborted));
                if (aborted) throw new IOException("휴지통 이동이 취소되었습니다. 교체하지 않았습니다.");
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
            finally
            {
                if (item is not null) Marshal.FinalReleaseComObject(item);
                if (operation is not null) Marshal.FinalReleaseComObject(operation);
            }
        }) { IsBackground = true, Name = "FileOrganizer.RecycleBin" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr context,
        ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IntPtr sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr dialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(uint owner);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        [PreserveSig] int RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IntPtr sink);
        [PreserveSig] int MoveItems(IntPtr items, IShellItem destination);
        [PreserveSig] int CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IntPtr sink);
        [PreserveSig] int CopyItems(IntPtr items, IShellItem destination);
        [PreserveSig] int DeleteItem(IShellItem item, IFileOperationProgressSink sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, IntPtr sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostRenameItem(uint flags, IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IntPtr newItem);
        [PreserveSig] int PreMoveItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostMoveItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IntPtr newItem);
        [PreserveSig] int PreCopyItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostCopyItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IntPtr newItem);
        [PreserveSig] int PreDeleteItem(uint flags, IntPtr item);
        [PreserveSig] int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr newItem);
        [PreserveSig] int PreNewItem(uint flags, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostNewItem(uint flags, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int result, IntPtr newItem);
        [PreserveSig] int UpdateProgress(uint total, uint complete);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class RecycleOnlySink : IFileOperationProgressSink
    {
        public bool RefusedPermanentDelete { get; private set; }
        // Shell이 휴지통 대신 영구 삭제를 선택하면 파일을 건드리기 전에 중단한다.
        public int PreDeleteItem(uint flags, IntPtr item)
        {
            if ((flags & 0x80) != 0) return 0;
            RefusedPermanentDelete = true;
            return unchecked((int)0x80004004);
        }
        public int StartOperations() => 0;
        public int FinishOperations(int result) => 0;
        public int PreRenameItem(uint flags, IntPtr item, string name) => 0;
        public int PostRenameItem(uint flags, IntPtr item, string name, int result, IntPtr newItem) => 0;
        public int PreMoveItem(uint flags, IntPtr item, IntPtr destination, string name) => 0;
        public int PostMoveItem(uint flags, IntPtr item, IntPtr destination, string name, int result, IntPtr newItem) => 0;
        public int PreCopyItem(uint flags, IntPtr item, IntPtr destination, string name) => 0;
        public int PostCopyItem(uint flags, IntPtr item, IntPtr destination, string name, int result, IntPtr newItem) => 0;
        public int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr newItem) => 0;
        public int PreNewItem(uint flags, IntPtr destination, string name) => 0;
        public int PostNewItem(uint flags, IntPtr destination, string name, string template, uint attributes, int result, IntPtr newItem) => 0;
        public int UpdateProgress(uint total, uint complete) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
}
