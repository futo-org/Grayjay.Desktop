namespace Grayjay.ClientServer.Database.Indexes
{
    [method: DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(DBIndex))]
    public abstract class DBIndex<T>
    {
        public virtual long ID { get; set; }
        public byte[] Serialized { get; set; }

        private T _obj;
        [Ignore]
        public T Object
        {
            get
            {
                EnsureDeserialized();
                return _obj;
            }
        }

        public abstract void FromObject(T obj);
        public abstract T Deserialize();

        public void EnsureDeserialized()
        {
            if (_obj == null)
            {
                if (Serialized == null)
                    throw new InvalidOperationException("This index does not contain the detail object (index-only)");
                _obj = Deserialize();
            }
        }
    }


    public class IgnoreAttribute : Attribute
    {
        public IgnoreAttribute() { }
    }

    public class OrderAttribute : Attribute
    {
        public int Priority { get; set; }
        public Ordering Ordering { get; set; }

        public OrderAttribute(int priority, Ordering ordering = Ordering.Ascending)
        {
            Priority = priority;
            Ordering = ordering;
        }
    }
    public class IndexedAttribute: Attribute
    {
        public Ordering Ordering { get; private set; }
        public IndexedAttribute(Ordering ordering = Ordering.None)
        {
            Ordering = ordering;
        }
    }
    public enum Ordering
    {
        None = 0,
        Ascending = 1,
        Descending = 2
    }
}
