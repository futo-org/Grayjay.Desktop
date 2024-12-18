namespace Grayjay.ClientServer.Models
{
    public class ToastDescriptor
    {
        public string Title { get; set; }
        public string Text { get; set; }
        public ToastDescriptor(string title, string text) 
        {
            Title = title;
            Text = text;
        }
        public ToastDescriptor(string text)
        {
            Text = text;
        }
    }
}
