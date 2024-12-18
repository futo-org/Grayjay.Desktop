using Grayjay.Engine.Pagers;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Grayjay.ClientServer.Pagers
{
    public abstract class ConvertPager<T, R> : INestedPager<T>, IPager<R>
    {
        public string ID { get { return _pager.ID; } set { _pager.ID = value; } }
        protected IPager<T> _pager;


        public ConvertPager(IPager<T> pager)
        {
            _pager = pager;
        }

        protected abstract R Convert(T item);


        public IPager<T> FindPager(Func<IPager<T>, bool> query)
        {
            if (query(_pager))
                return _pager;
            else if (_pager is INestedPager<T>)
                return ((_pager as INestedPager<T>) ?? throw new InvalidOperationException()).FindPager(query);
            return null;
        }

        public R[] GetResults()
        {
            return _pager.GetResults().Select(x => Convert(x)).ToArray();
        }

        public bool HasMorePages()
        {
            return _pager.HasMorePages();
        }

        public void NextPage()
        {
            _pager.NextPage();
        }
    }

    public class SelectPager<T, R>: ConvertPager<T, R>
    {
        private Func<T, R> _conversion;
        public SelectPager(IPager<T> pager, Func<T, R> conversion) : base(pager)
        {
            _conversion = conversion;
        }

        protected override R Convert(T item)
        {
            return _conversion(item);
        }
    }
}
