using System;
using System.Collections;
using System.Collections.Generic;
using Unity.HLODSystem.Utils;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.HLODSystem.Streaming
{
    public class AddressableHLODController : HLODControllerBase
    {
        [Serializable]
        public class ChildObject
        {
            public GameObject  gameObject;
            public int         meshIndex;
            public int[]       materialIndices;
        }

        [SerializeField]
        private List<ChildObject> m_highObjects = new List<ChildObject>();

        [SerializeField]
        private List<string> m_lowObjects = new List<string>();

        [SerializeField]
        int m_priority = 0;
        
        class LoadInfo
        {
            public GameObject GameObject;
            public AddressableLoadManager.HandleBase[] Handles;
            public List<Action<GameObject>> Callbacks;
        }

        private Dictionary<int, LoadInfo> m_createdHighObjects = new Dictionary<int, LoadInfo>();
        private Dictionary<int, LoadInfo> m_createdLowObjects = new Dictionary<int, LoadInfo>();

        private GameObject m_hlodMeshesRoot;
        private int m_hlodLayerIndex;

        public event Action<GameObject> HighObjectCreated;

        private Dictionary<string, int> m_Resources;

        [SerializeField]
        private List<string> m_ResourceAddresses;

        public delegate string ResolveAddress(Object asset);
        
        public override void OnStart()
        {
            m_hlodMeshesRoot = new GameObject("HLODMeshesRoot");
            m_hlodMeshesRoot.transform.SetParent(transform, false);

            m_hlodLayerIndex = LayerMask.NameToLayer(HLOD.HLODLayerStr);

            AddressableLoadManager.Instance.RegisterController(this);

        }

        public override void OnStop()
        {
            if ( AddressableLoadManager.Instance != null)
                AddressableLoadManager.Instance.UnregisterController(this);
        }


        public override void Install()
        {
            for (int i = 0; i < m_highObjects.Count; ++i)
            {
                StripGameObject(m_highObjects[i].gameObject);
                m_highObjects[i].gameObject.SetActive(false);
            }

            m_Resources = null;
        }

        public int AddHighObject(GameObject gameObject, ResolveAddress resolveAddress)
        {
            int id = m_highObjects.Count;

            ChildObject obj = new ChildObject();
            obj.gameObject = gameObject;

            var meshFilter = gameObject.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                obj.meshIndex = AddResource(resolveAddress(meshFilter.sharedMesh));
            }

            var meshRenderer = gameObject.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                obj.materialIndices = new int[meshRenderer.sharedMaterials.Length];
                for (var i = 0; i < obj.materialIndices.Length; i++)
                {
                    obj.materialIndices[i] = AddResource(resolveAddress(meshRenderer.sharedMaterials[i]));
                }
            }

            m_highObjects.Add(obj);
            return id;
        }
        
        public int AddLowObject(string address)
        {
            int id = m_lowObjects.Count;
            m_lowObjects.Add(address);
            return id;
        }

        public override int HighObjectCount { get => m_highObjects.Count; }
        public override int LowObjectCount { get => m_lowObjects.Count; }

        public string GetLowObjectAddr(int index) { return m_lowObjects[index]; }

        public override void GetHighObject(int id, int level, float distance, Action<GameObject> loadDoneCallback)
        {
            //already processing object to load.
            if (m_createdHighObjects.ContainsKey(id) == true)
            {
                //already load done.
                if (m_createdHighObjects[id].GameObject != null)
                {
                    loadDoneCallback?.Invoke(m_createdHighObjects[id].GameObject);

                }
                //not finished loading yet.
                else
                {
                    m_createdHighObjects[id].Callbacks.Add(loadDoneCallback);
                }
            }
            else
            {
                //high object's priority is always lowest.
                LoadInfo loadInfo = CreateLoadInfo(m_highObjects[id], m_priority, distance);
                m_createdHighObjects.Add(id, loadInfo);
                
                loadInfo.Callbacks = new List<Action<GameObject>>();
                loadInfo.Callbacks.Add(loadDoneCallback);
                loadInfo.Callbacks.Add(o => { HighObjectCreated?.Invoke(o); });                
            }            
            
        }


        public override void GetLowObject(int id, int level, float distance, Action<GameObject> loadDoneCallback)
        {
            //already processing object to load.
            if (m_createdLowObjects.ContainsKey(id) == true)
            {
                //already load done.
                if (m_createdLowObjects[id].GameObject != null)
                {
                    loadDoneCallback?.Invoke(m_createdLowObjects[id].GameObject);

                }
                //not finished loading yet.
                else
                {
                    m_createdLowObjects[id].Callbacks.Add(loadDoneCallback);
                }
            }
            else
            {
                LoadInfo loadInfo = CreateLoadInfo(m_lowObjects[id], m_priority, distance, m_hlodMeshesRoot.transform, Vector3.zero, Quaternion.identity,Vector3.one);
                m_createdLowObjects.Add(id, loadInfo);

                loadInfo.Callbacks = new List<Action<GameObject>>();
                loadInfo.Callbacks.Add(loadDoneCallback);
            }
        }

        public override void ReleaseHighObject(int id)
        {
            if (m_createdHighObjects.ContainsKey(id) == false)
                return;
            
            StripGameObject(m_createdHighObjects[id].GameObject);
            m_createdHighObjects[id].GameObject.SetActive(false);
            LoadInfo info = m_createdHighObjects[id];
            for (int i = 0; i < info.Handles.Length; i ++)
            {
                AddressableLoadManager.Instance.UnloadAsset(info.Handles[i]);    
            }
            m_createdHighObjects.Remove(id);
        }

        public override void ReleaseLowObject(int id)
        {
            if (m_createdLowObjects.ContainsKey(id) == false)
                return;
            
            LoadInfo info = m_createdLowObjects[id];
            m_createdLowObjects.Remove(id);
            
            DestoryObject(info.GameObject);
            for (int i = 0; i < info.Handles.Length; i ++)
            {
                AddressableLoadManager.Instance.UnloadAsset(info.Handles[i]);    
            }
        }

        private void DestoryObject(Object obj)
        {
#if UNITY_EDITOR
            DestroyImmediate(obj);
#else
            Destroy(obj);
#endif
        }
        
        // For high objects
        private LoadInfo CreateLoadInfo(ChildObject childObj, int priority, float distance)
        {
            var meshAddr = GetMeshAddressForHighObject(childObj);
            var matAddrs = GetMaterialAddressesForHighObject(childObj);

            LoadInfo loadInfo = new LoadInfo();
            loadInfo.Handles = new AddressableLoadManager.HandleBase[2];
            loadInfo.Handles[0] = AddressableLoadManager.Instance.LoadAsset<Mesh>(this, meshAddr, priority, distance);
            loadInfo.Handles[1] = AddressableLoadManager.Instance.LoadAssets<Material>(this, matAddrs, priority, distance);

            loadInfo.Handles[0].Completed += handle =>
            {
                if (handle.Status == AsyncOperationStatus.Failed)
                {
                    Debug.LogError("Failed to load assets: " + meshAddr);
                    return;
                }
                var assets = (handle as AddressableLoadManager.Handle<Mesh>).Result;
                var gameObject = childObj.gameObject;

                var meshFilter = gameObject.GetComponent<MeshFilter>();
                if (meshFilter != null)
                {
                    meshFilter.sharedMesh = assets[0];
                }

                CheckLoadComplete(loadInfo, gameObject);
            };

            loadInfo.Handles[1].Completed += handle =>
            {
                if (handle.Status == AsyncOperationStatus.Failed)
                {
                    Debug.LogError("Failed to load assets: " + String.Join(",", matAddrs));
                    return;
                }
                var assets = (handle as AddressableLoadManager.Handle<Material>).Result;;
                var gameObject = childObj.gameObject;
                var materials = new Material[assets.Count];
                for (var i = 0; i < assets.Count; i++)
                {
                    materials[i] = assets[i] as Material;
                }
                var meshRenderer = gameObject.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.sharedMaterials = materials;
                }

                CheckLoadComplete(loadInfo, gameObject);
            };            
            return loadInfo;
        }

        // For low objects
        private LoadInfo CreateLoadInfo(string address, int priority, float distance, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            LoadInfo loadInfo = new LoadInfo();
            loadInfo.Handles = new AddressableLoadManager.HandleBase[1];
            loadInfo.Handles[0] = AddressableLoadManager.Instance.LoadAsset<GameObject>(this, address, priority, distance);

            loadInfo.Handles[0].Completed += handle =>
            {
                if (handle.Status == AsyncOperationStatus.Failed)
                {
                    Debug.LogError("Failed to load asset: " + address);
                    return;
                }
   
                GameObject gameObject = Instantiate((handle as AddressableLoadManager.Handle<GameObject>).Result[0], parent, false);
                gameObject.transform.localPosition = localPosition;
                gameObject.transform.localRotation = localRotation;
                gameObject.transform.localScale = localScale;

                CheckLoadComplete(loadInfo, gameObject);
            };
            return loadInfo;
        }

        void CheckLoadComplete(LoadInfo info, GameObject gameObject)
        {
            if (info.GameObject != null)
            {
                return;
            }

            for (int i = 0; i < info.Handles.Length; ++i)
            {
                if (info.Handles[i].Status != AsyncOperationStatus.Succeeded)
                {
                    return;
                }
            }

            gameObject.SetActive(false);
            ChangeLayersRecursively(gameObject.transform, m_hlodLayerIndex);
            info.GameObject = gameObject;
                
            for (int i = 0; i < info.Callbacks.Count; ++i)
            {
                info.Callbacks[i]?.Invoke(gameObject);
            }
            info.Callbacks.Clear();
        }

        static void ChangeLayersRecursively(Transform trans, int layer)
        {
            trans.gameObject.layer = layer;
            foreach (Transform child in trans)
            {
                ChangeLayersRecursively(child, layer);
            }
        }

        static void StripGameObject(GameObject gameObject)
        {
            var meshFilter = gameObject.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                meshFilter.sharedMesh = null;
            }

            var meshRenderer = gameObject.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.sharedMaterials = new Material[0];
            }
        }

        public int AddResource(string address)
        {
            #if UNITY_EDITOR
                if (m_Resources == null)
                {
                    m_Resources = new Dictionary<string, int>();
                }

                if (string.IsNullOrEmpty(address))
                {
                    return -1;
                }

                int id;
                if (!m_Resources.TryGetValue(address, out id))
                {
                    id = m_Resources.Count;
                    m_Resources.Add(address, id);

                    if (m_ResourceAddresses == null)
                    {
                        m_ResourceAddresses = new List<string>();
                    }
                    m_ResourceAddresses.Add(address);
                }

                return (int)id;
            #else
                return -1;
            #endif
        }

        public string GetMeshAddressForHighObject(ChildObject childObj)
        {
            return m_ResourceAddresses[childObj.meshIndex];
        }

        public string[] GetMaterialAddressesForHighObject(ChildObject childObj)
        {
            string[] addresses = new string[childObj.materialIndices.Length];
            for (int i = 0; i < addresses.Length; i++)
            {
                addresses[i] = m_ResourceAddresses[childObj.materialIndices[i]];
            }
            return addresses;
        }
    }

}