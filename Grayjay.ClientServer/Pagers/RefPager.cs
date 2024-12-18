using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Pagers;

namespace Grayjay.ClientServer.Pagers
{
    public class AnonymousContentRefPager : ConvertPager<RefItem<PlatformContent>, PlatformContent>
    {
        private RefPager<PlatformContent> _refPager => _pager as RefPager<PlatformContent>;

        public AnonymousContentRefPager(IPager<PlatformContent> subPager) : base(new RefPager<PlatformContent>(subPager))
        {

        }

        protected override PlatformContent Convert(RefItem<PlatformContent> item)
        {
            var content = item.Object;
            if(content is IPlatformContentDetails)
            {
                content.BackendUrl = $"https://grayjay.internal/refPager?pagerId={_refPager.ID}&itemId={item.RefID}";
            }
            return content;
        }
    }

    public class RefPager<T> : ReusablePager<RefItem<T>>
    {
        public RefPager(IPager<T> subPager) : base(new RefConvertPager<T>(subPager))
        {
            if(typeof(T) == typeof(PlatformContent))
                StatePlatform.RegisterRefPager((RefPager<PlatformContent>)(object)this);
        }

        public RefItem<T> FindRef(string refId, bool throwIfNull = false)
        {
            var result = PreviousResults.FirstOrDefault(x => x.RefID == refId);
            if (result == null && throwIfNull)
            {
                throw new ArgumentException($"RefID [{refId}] does not exist in pager");   
            }
            return result;
        }


        class RefConvertPager<T> : ConvertPager<T, RefItem<T>>
        {
            public RefConvertPager(IPager<T> pager) : base(pager) { }

            protected override RefItem<T> Convert(T item)
            {
                return new RefItem<T>(Guid.NewGuid().ToString(), item);
            }
        }
    }

    public class RefPager<T, R> : ReusablePager<R>
    {
        public RefPager(IPager<T> subPager, Func<T, string, R> conversion) : base(new RefConvertPager<T>(subPager, conversion)) { }


        class RefConvertPager<T> : ConvertPager<T, R>
        {
            private Func<T, string, R> _conversion;
            public RefConvertPager(IPager<T> pager, Func<T, string, R> conversion) : base(pager) 
            {
                _conversion = conversion;
            }

            protected override R Convert(T item)
            {
                return _conversion(item, Guid.NewGuid().ToString());
            }
        }
    }

    public class RefItem<T>
    {
        public string RefID { get; set; }
        public T Object { get; set; }

        public RefItem() { }
        public RefItem(string refID, T obj)
        {
            RefID = refID;
            Object = obj;
        }
    }
}
