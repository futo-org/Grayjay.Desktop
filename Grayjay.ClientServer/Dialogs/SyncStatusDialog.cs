namespace Grayjay.ClientServer.Dialogs
{
    public class SyncStatusDialog : RemoteDialog
    {
        public string? Message { get; set; }

        public SyncStatusDialog(): base("syncStatus")
        {
            Status = "pairing";
        }

        public async override Task Show()
        {
            await base.Show();
        }

        public void SetMessage(string message)
        {
            Message = message;
            Status = "success";
            Update();
        }

        public void SetSuccess()
        {
            Status = "success";
            Update();
        }

        public void SetError(string message)
        {
            Message = message;
            Status = "error";
            Update();
        }
    }
}
