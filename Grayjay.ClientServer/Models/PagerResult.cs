namespace Grayjay.ClientServer.Models
{
    public class PagerResult<T>
    {
        public string PagerID { get; set; }
        public T[] Results { get; set; }
        public bool HasMore { get; set; }
        public string Exception { get; set; }
    }
}
