using System;
using System.Collections.Generic;
using Unity.HLODSystem.Streaming;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;



namespace Unity.HLODSystem
{
    
    public class AddressableLoadManager : MonoBehaviour
    {
        public abstract class HandleBase
        {
            public HandleBase(AddressableHLODController controller, string address, int priority, float distance)
            {
                m_controller = controller;
                m_addresses = new string[] { address };
                m_priority = priority;
                m_distance = distance;
            }

            public HandleBase(AddressableHLODController controller, string[] addresses, int priority, float distance)
            {
                m_controller = controller;
                m_addresses = addresses;
                m_priority = priority;
                m_distance = distance;
            }

            protected AddressableHLODController m_controller;
            protected string[] m_addresses;
            protected int m_priority;
            protected float m_distance;
            protected bool m_startLoad = false;

            public string[] Addresses => m_addresses;

            public int Priority
            {
                get { return m_priority; }
            }

            public float Distance
            {
                get { return m_distance; }
            }

            public AddressableHLODController Controller
            {
                get { return m_controller; }
            }

            public event Action<HandleBase> Completed;

            public void Start()
            {
                m_startLoad = true;
                LoadImpl();
            }

            public void Stop()
            {
                ReleaseImpl();
            }

            public AsyncOperationStatus Status
            {
                get
                {
                    if (m_startLoad == false)
                    {
                        return AsyncOperationStatus.None;
                    }

                    return GetAsyncHandleStatusImpl();
                }
            }

            protected void OnLoadComplete()
            {
                Completed?.Invoke(this);
            }

            protected abstract void LoadImpl();
            protected abstract void ReleaseImpl();
            protected abstract AsyncOperationStatus GetAsyncHandleStatusImpl();
        }

        public class Handle<T>: HandleBase
        {
            public IList<T> Result
            {
                get { return m_asyncHandle.Result; }
            }

            public Handle(AddressableHLODController controller, string address, int priority, float distance): base(controller, address, priority, distance)
            {
            }

            public Handle(AddressableHLODController controller, string[] addresses, int priority, float distance):  base(controller, addresses, priority, distance)
            {
            }

            protected override void LoadImpl()
            {
                m_asyncHandle = Addressables.LoadAssetsAsync<T>(m_addresses, null, Addressables.MergeMode.None);
                m_asyncHandle.Completed += handle =>
                {
                    this.OnLoadComplete();
                };
            }

            protected override void ReleaseImpl()
            {
                if (m_startLoad)
                {
                    Addressables.Release(m_asyncHandle);
                }
            }

            protected override AsyncOperationStatus GetAsyncHandleStatusImpl()
            {
                return m_asyncHandle.Status;
            }

            private AsyncOperationHandle<IList<T>> m_asyncHandle;
        }
        #region Singleton
        private static AddressableLoadManager s_instance;
        private static bool s_isDestroyed = false;
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void OnLoad()
        {
            s_instance = null;
            s_isDestroyed = false;
        }
        public static AddressableLoadManager Instance
        {
            get
            {
                if (s_isDestroyed)
                    return null;
                
                if (s_instance == null)
                {
                    GameObject go = new GameObject("AddressableLoadManager");
                    s_instance = go.AddComponent<AddressableLoadManager>();
                    DontDestroyOnLoad(go);
                }

                return s_instance;
            }
        }
        #endregion


        private bool m_isLoading = false;
        private LinkedList<HandleBase> m_loadQueue = new LinkedList<HandleBase>();

        private void OnDestroy()
        {
            s_isDestroyed = true;
        }

        public void RegisterController(AddressableHLODController controller)
        {
        }

        public void UnregisterController(AddressableHLODController controller)
        {
            var node = m_loadQueue.First;
            while (node != null)
            {
                if (node.Value.Controller == controller)
                {
                    var remove = node;
                    node = node.Next;
                    m_loadQueue.Remove(remove);
                }
                else
                {
                    node = node.Next;
                }
            }

        }

        public HandleBase LoadAsset<T>(AddressableHLODController controller, string address, int priority, float distance)
        {
            HandleBase handle = new Handle<T>(controller, address, priority, distance);
            InsertHandle(handle);
            return handle;
        }

        public HandleBase LoadAssets<T>(AddressableHLODController controller, string[] addresses, int priority, float distance)
        {
            HandleBase handle = new Handle<T>(controller, addresses, priority, distance);
            InsertHandle(handle);
            return handle;
        }

        public void UnloadAsset(HandleBase handle)
        {
            m_loadQueue.Remove(handle);
            handle.Stop();
        }

        private void InsertHandle(HandleBase handle)
        {
            var node = m_loadQueue.First;
            while (node != null && node.Value.Priority < handle.Priority)
            {
                node = node.Next;
            }

            while (node != null && node.Value.Priority == handle.Priority && node.Value.Distance < handle.Distance)
            {
                node = node.Next;
            }

            if (node == null)
            {
                if (m_isLoading == true)
                {
                    m_loadQueue.AddLast(handle);
                }
                else
                {
                    StartLoad(handle);
                }
            }
            else
            {
                m_loadQueue.AddBefore(node, handle);
            }
        }

        private void StartLoad(HandleBase handle)
        {
            handle.Completed += handle1 =>
            {
                m_isLoading = false;
                if (m_loadQueue.Count > 0)
                {
                    HandleBase nextHandle = m_loadQueue.First.Value;
                    m_loadQueue.RemoveFirst();
                    StartLoad(nextHandle);
                }
            };
            m_isLoading = true;
            handle.Start();
        }
   
    }
}