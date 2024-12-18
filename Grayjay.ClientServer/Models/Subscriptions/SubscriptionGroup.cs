namespace Grayjay.ClientServer.Models.Subscriptions
{
    public class SubscriptionGroup
    {
        public string ID { get; set; }
        public string Name { get; set; }
        public ImageVariable Image { get; set; }
        public List<string> Urls { get; set; }
        public int Priority { get; set; }

        public DateTime LastChange { get; set; } = DateTime.MinValue;
        public DateTime CreationTime { get; set; } = DateTime.Now;
    }

    public class ImageVariable
    {
        public string Url { get; set; }
        public int ResId { get; set; }
        public string PresetName { get; set; }
    }
}
