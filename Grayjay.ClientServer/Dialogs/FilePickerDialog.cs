using System.Text.Json;
using Grayjay.Desktop.POC;
using Newtonsoft.Json;
using static Grayjay.ClientServer.Controllers.StateUI;

namespace Grayjay.ClientServer.Dialogs
{
    public class FilePickerDialog : RemoteDialog
    {
        public class Filter
        {
            public required string Name { get; set; }
            public required string Pattern { get; set; }
        }

        public bool AllowMultiple { get; set; }
        public string? DefaultFileName { get; set; }
        public string Mode { get; set; }
        public string SelectionMode { get; set; }
        public Filter[]? Filters { get; set; }

        private readonly Action<string[]> _callback;

        public FilePickerDialog(string mode, string selectionMode, Filter[]? filters, bool allowMultiple, string? defaultFileName, Action<string[]> callback) : base("filePicker")
        {
            Mode = mode;
            SelectionMode = selectionMode;
            Filters = filters;
            DefaultFileName = defaultFileName;
            AllowMultiple = allowMultiple;
            _callback = callback;
        }

        public async override Task Show()
        {
            await base.Show();
        }

        [DialogMethod("pick")]
        public void Dialog_Pick(CustomDialog dialog, JsonElement parameter)
        {
            Logger.i(nameof(FilePickerDialog), "Pick: " + parameter.ToString());
            _callback.Invoke(parameter.Deserialize<string[]>() ?? []);
        }

        public static FilePickerDialog OpenFilePicker(Action<string[]> callback, bool allowMultiple = false, Filter[]? filters = null) => new FilePickerDialog("open", "file", filters, allowMultiple, null, callback);
        public static FilePickerDialog OpenFolderPicker(Action<string[]> callback, bool allowMultiple = false, Filter[]? filters = null) => new FilePickerDialog("open", "folder", filters, allowMultiple, null, callback);
        public static FilePickerDialog SaveFilePicker(Action<string[]> callback, string? defaultFileName, Filter[]? filters = null) => new FilePickerDialog("save", "file", filters, false, defaultFileName, callback);
    }
}
