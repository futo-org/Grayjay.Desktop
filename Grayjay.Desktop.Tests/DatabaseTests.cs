using Grayjay.ClientServer.Database;
using Grayjay.ClientServer.Database.Indexes;
using Grayjay.ClientServer.Store;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Grayjay.Desktop.Tests
{
    [TestClass]
    public class DatabaseTests
    {
        [TestMethod]
        public void Test_Startup()
        {
            TestDatabase((store) =>
            {
                Assert.AreEqual(0, store.Count());
            });
        }

        [TestMethod]
        public void Test_Insert()
        {
            TestDatabase((store) =>
            {
                var obj = new TestClass();
                var index = CreateAndAssert(store, obj);
                Assert.AreEqual(1, store.Count());
            });
        }
        [TestMethod]
        public void Test_Insert_x500()
        {
            int count = 500;
            TestDatabase((store) =>
            {
                Stopwatch watch = Stopwatch.StartNew();
                var obj = new TestClass();
                for(int i = 0; i < count; i++)
                    CreateAndAssert(store, obj);
                Assert.AreEqual(count, store.Count());
                watch.Stop();
                Console.WriteLine($"Insert time: {watch.ElapsedMilliseconds}ms, Per item: {watch.ElapsedMilliseconds / count}ms");
            });
        }
        [TestMethod]
        public void Test_Update()
        {
            TestDatabase((store) =>
            {
                var obj = new TestClass();
                var obj2 = new TestClass();
                
                var index = CreateAndAssert(store, obj);
                var index2 = CreateAndAssert(store, obj);
                Assert.AreEqual(2, store.Count());

                var updated = index.Deserialize();
                updated.String = Guid.NewGuid().ToString();
                updated.String2 = Guid.NewGuid().ToString();
                store.Update(index.ID, updated);

                var fetched = store.Get(index.ID);
                AssertIndexEquals(fetched, updated);
            });
        }
        [TestMethod]
        public void Test_Update_x500()
        {
            int count = 500;
            TestDatabase((store) =>
            {
                List<TestIndex> items = new List<TestIndex>();
                for (int i = 0; i < count; i++)
                    items.Add(CreateAndAssert(store, new TestClass()));
                List<TestIndex> fetchedItems = store.GetAll(true);

                Stopwatch watch = new Stopwatch();
                watch.Start();
                foreach(TestIndex item in fetchedItems)
                {
                    var updated = item.Object;
                    updated.String = Guid.NewGuid().ToString();
                    updated.String2 = Guid.NewGuid().ToString();
                    store.Update(item.ID, updated);
                }
                watch.Stop();
                Console.WriteLine($"Update time: {watch.ElapsedMilliseconds}ms, Per item: {watch.ElapsedMilliseconds / count}ms");
            });
        }
        [TestMethod]
        public void Test_Delete()
        {
            TestDatabase((store) =>
            {
                var obj1 = new TestClass();
                var obj2 = new TestClass();
                var index = CreateAndAssert(store, obj1);
                var index2 = CreateAndAssert(store, obj2);
                Assert.AreEqual(2, store.Count());
                store.Delete(index);
                Assert.AreEqual(1, store.Count());

                var fetched = store.Get(index2.ID);
                Assert.IsNotNull(fetched);
            });
        }

        [TestMethod]
        public void Test_WithIndex()
        {
            var index = new ConcurrentDictionary<object, TestIndex>();
            TestDatabase((builder)=>builder.WithIndex(x=>x.String, index, true),(store) =>
            {
                var testObj1 = new TestClass();
                var testObj2 = new TestClass();
                var testObj3 = new TestClass();
                var obj1 = CreateAndAssert(store, testObj1);
                var obj2 = CreateAndAssert(store, testObj2);
                var obj3 = CreateAndAssert(store, testObj3);
                //Creation
                Assert.AreEqual(3, store.Count());
                Assert.IsTrue(index.ContainsKey(testObj1.String));
                Assert.IsTrue(index.ContainsKey(testObj2.String));
                Assert.IsTrue(index.ContainsKey(testObj3.String));

                //Update
                var oldStr = testObj1.String;
                testObj1.String = Guid.NewGuid().ToString();
                store.Update(obj1.ID, testObj1);
                Assert.AreEqual(3, store.Count());
                Assert.IsFalse(index.ContainsKey(oldStr));
                Assert.IsTrue(index.ContainsKey(testObj1.String));
                Assert.IsTrue(index.ContainsKey(testObj2.String));
                Assert.IsTrue(index.ContainsKey(testObj3.String));

                //Delete
                store.Delete(obj2.ID);
                Assert.AreEqual(2, index.Count);
                Assert.IsFalse(index.ContainsKey(oldStr));
                Assert.IsTrue(index.ContainsKey(testObj1.String));
                Assert.IsFalse(index.ContainsKey(testObj2.String));
                Assert.IsTrue(index.ContainsKey(testObj3.String));
            });
        }

        [TestMethod]
        public void Test_WithUnique()
        {
            var index = new ConcurrentDictionary<object, TestIndex>();
            TestDatabase((builder) => builder.WithUnique(x => x.String, index), store =>
            {
                var testObj1 = new TestClass();
                var testObj2 = new TestClass();
                var testObj3 = new TestClass();
                var obj1 = CreateAndAssert(store, testObj1);
                var obj2 = CreateAndAssert(store, testObj2);

                testObj3.String = obj2.String;
                Assert.AreEqual(obj2.ID, store.Insert(testObj3).ID);
                Assert.AreEqual(2, store.Count());
            });
        }


        [TestMethod]
        public void Test_Pager()
        {
            TestDatabase(store =>
            {
                //0,1,2...24,25
                var testObjs = CreateSequence(store, 25);

                var pager = store.Pager(10);

                var page1 = pager.GetResults();
                Assert.IsTrue(pager.HasMorePages());
                Assert.AreEqual(page1.Length, 10);
                for (int i = 0; i < 10; i++)
                    Assert.AreEqual(i, page1[i].Integer);

                pager.NextPage();
                var page2 = pager.GetResults();
                Assert.IsTrue(pager.HasMorePages());
                Assert.AreEqual(page2.Length, 10);
                for (int i = 10; i < 20; i++)
                    Assert.AreEqual(i, page2[i - 10].Integer);

                pager.NextPage();
                var page3 = pager.GetResults();
                Assert.AreEqual(page3.Length, 5);
                for (int i = 20; i < 25; i++)
                    Assert.AreEqual(i, page3[i - 20].Integer);
            });
        }


        [TestMethod]
        public void Test_Query()
        {
            TestDatabase(store =>
            {
                var str = Guid.NewGuid().ToString();

                //0,2,4...22,24
                var testObjs = CreateSequence(store, 25, (y, i) =>
                {
                    if (y.Integer % 2 == 0)
                        y.String = str;
                });

                var results = store.Query(nameof(TestIndex.String), str);
                Assert.AreEqual(13, results.Length);
            });
        }
        [TestMethod]
        public void Test_QueryPager()
        {
            TestDatabase(store =>
            {
                var str = Guid.NewGuid().ToString();

                //0,2,4...22,24
                var testObjs = CreateSequence(store, 25, (y, i) =>
                {
                    if (y.Integer % 2 == 0)
                        y.String = str;
                });

                var pager = store.QueryPager(nameof(TestIndex.String), str, 10);

                var page1 = pager.GetResults();
                Assert.IsTrue(pager.HasMorePages());
                Assert.AreEqual(page1.Length, 10);
                for (int i = 0; i < 10; i++)
                    Assert.AreEqual(i * 2, page1[i].Integer);

                pager.NextPage();
                var page2 = pager.GetResults();
                Assert.IsTrue(pager.HasMorePages());
                Assert.AreEqual(page2.Length, 3);
                for (int i = 10; i < 13; i++)
                    Assert.AreEqual(i * 2, page2[i - 10].Integer);

                pager.NextPage();
                Assert.IsFalse(pager.HasMorePages());
            });
        }

        [TestMethod]
        public void Test_QueryIn()
        {
            TestDatabase(store =>
            {
                var str = Guid.NewGuid().ToString();
                var str2 = Guid.NewGuid().ToString();

                //0,2,4...22,24
                var testObjs = CreateSequence(store, 26, (y, i) =>
                {
                    if (y.Integer % 2 == 0)
                        y.String = str;
                    else if (y.Integer == 25)
                        y.String = str2;
                });

                var results = store.QueryIn(nameof(TestIndex.String), new[] { str, str2 });
                Assert.AreEqual(14, results.Length);
            });
        }
        [TestMethod]
        public void Test_QueryInPager()
        {
            TestDatabase(store =>
            {
                var str = Guid.NewGuid().ToString();
                var str2 = Guid.NewGuid().ToString();

                //0,2,4...22,24
                var testObjs = CreateSequence(store, 26, (y, i) =>
                {
                    if (y.Integer % 2 == 0)
                        y.String = str;
                    else if (y.Integer == 25)
                        y.String = str2;
                });

                var pager = store.QueryInPager(nameof(TestIndex.String), new [] {str, str2 }, 10);

                var page1 = pager.GetResults();
                Assert.IsTrue(pager.HasMorePages());
                Assert.AreEqual(page1.Length, 10);
                for (int i = 0; i < 10; i++)
                    Assert.AreEqual(i * 2, page1[i].Integer);

                pager.NextPage();
                var page2 = pager.GetResults();
                Assert.IsTrue(pager.HasMorePages());
                Assert.AreEqual(page2.Length, 4);
                for (int i = 10; i < 13; i++)
                    Assert.AreEqual(i * 2, page2[i - 10].Integer);
                Assert.AreEqual(25, page2[3].Integer);


                pager.NextPage();
                Assert.IsFalse(pager.HasMorePages());
            });
        }


        [TestMethod]
        public void Test_QueryLike()
        {
            TestDatabase(store =>
            {
                var str = Guid.NewGuid().ToString() + "Testing" + Guid.NewGuid().ToString();

                //0,2,4...22,24
                var testObjs = CreateSequence(store, 26, (y, i) =>
                {
                    if (y.Integer % 2 == 0)
                        y.String = str;
                });

                var results = store.QueryLike(nameof(TestIndex.String), "%Testing%");
                Assert.AreEqual(13, results.Length);
            });
        }
        [TestMethod]
        public void Test_QueryLikePager()
        {
            TestDatabase(store =>
            {
                var str = Guid.NewGuid().ToString() + "Testing" + Guid.NewGuid().ToString();

                //0,2,4...22,24
                var testObjs = CreateSequence(store, 26, (y, i) =>
                {
                    if (y.Integer % 2 == 0)
                        y.String = str;
                });

                var pager = store.QueryLikePager(nameof(TestIndex.String), "%Testing%", 10);

                var page1 = pager.GetResults();
                Assert.IsTrue(pager.HasMorePages());
                Assert.AreEqual(page1.Length, 10);
                for (int i = 0; i < 10; i++)
                    Assert.AreEqual(i * 2, page1[i].Integer);

                pager.NextPage();
                var page2 = pager.GetResults();
                Assert.IsTrue(pager.HasMorePages());
                Assert.AreEqual(page2.Length, 3);
                for (int i = 10; i < 13; i++)
                    Assert.AreEqual(i * 2, page2[i - 10].Integer);

                pager.NextPage();
                Assert.IsFalse(pager.HasMorePages());
            });
        }






        //---------------------------------
        //Utility
        //---------------------------------
        private static TestIndex CreateAndAssert(ManagedDBStore<TestIndex, TestClass> store, TestClass obj)
        {
            var index = store.Insert(obj);
            Assert.IsTrue(index.ID >= 0);

            var dbObj = store.Get(index.ID);
            AssertIndexEquals(dbObj, obj);
            return dbObj;
        }
        private static List<TestIndex> CreateSequence(ManagedDBStore<TestIndex, TestClass> store, int count, Action<TestClass, int> modifier = null)
        {
            List<TestIndex> indexes = new List<TestIndex>();
            for(int i = 0; i < count; i++)
            {
                var obj = new TestClass();
                obj.Integer = i;
                modifier?.Invoke(obj, i);
                indexes.Add(CreateAndAssert(store, obj));
            }
            return indexes;
        }

        

        private static void AssertIndexEquals(TestIndex index1, TestIndex index2)
        {
            Assert.AreEqual(index1.String, index2.String);
            Assert.AreEqual(index1.Integer, index2.Integer);
            AssertObjectEquals(index1.Deserialize(), index2.Deserialize());
        }
        private static void AssertIndexEquals(TestIndex index1, TestClass obj)
        {
            Assert.AreEqual(index1.String, obj.String);
            Assert.AreEqual(index1.Integer, obj.Integer);
            AssertObjectEquals(index1.Deserialize(), obj);
        }
        private static void AssertObjectEquals(TestClass obj1, TestClass obj2)
        {
            Assert.AreEqual(obj1.String, obj2.String);
            Assert.AreEqual(obj1.String2, obj2.String2);
            Assert.AreEqual(obj1.Integer, obj2.Integer);
            Assert.AreEqual(obj1.Abc, obj2.Abc);
        }

        private static void TestDatabase(Action<ManagedDBStore<TestIndex, TestClass>> builder, Action<ManagedDBStore<TestIndex, TestClass>> action)
        {
            var connection = new DatabaseConnection();
            connection.EnsureTable<TestIndex>("test");
            var store = new ManagedDBStore<TestIndex, TestClass>(connection, "test");
            builder(store);
            store.Load();
            store.DeleteAll();
            action(store);
            store.Dispose();
        }
        private static void TestDatabase(Action<ManagedDBStore<TestIndex, TestClass>> action)
        {
            var connection = new DatabaseConnection();
            connection.EnsureTable<TestIndex>("test");
            var store = new ManagedDBStore<TestIndex, TestClass>(connection, "test");
            store.Load();
            store.DeleteAll();
            action(store);
            store.Dispose();
        }
    }

    public class TestIndex: DBIndex<TestClass>
    {
        [Order(0)]
        public int Integer { get; set; }
        public string String { get; set; }

        public override TestClass Deserialize()
        {
            return JsonSerializer.Deserialize<TestClass>(Serialized);
        }

        public override void FromObject(TestClass obj)
        {
            Integer = obj.Integer;
            String = obj.String;
            Serialized = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj));
        }
    }

    public class TestClass
    {
        private static Random _r = new Random();
        public string String { get; set; } = Guid.NewGuid().ToString();
        public string String2 { get; set; } = Guid.NewGuid().ToString();
        public int Integer { get; set; } = _r.Next();
        public string Abc { get; set; } = Guid.NewGuid().ToString();
    }
}
