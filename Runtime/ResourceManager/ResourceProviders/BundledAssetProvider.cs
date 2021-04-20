using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.Util;

namespace UnityEngine.ResourceManagement.ResourceProviders
{
    /// <summary>
    /// Provides assets stored in an asset bundle.
    /// </summary>
    [DisplayName("Assets from Bundles Provider")]
    public class BundledAssetProvider : ResourceProviderBase
    {
        internal class InternalOp
        {
            AssetBundleRequest m_RequestOperation;
            object m_Result;
            ProvideHandle m_ProvideHandle;
            string subObjectName = null;
            
            internal static IAssetBundleResource LoadBundleFromDependecies(IList<object> results)
            {
                if (results == null || results.Count == 0)
                    return null;

                IAssetBundleResource bundle = null;
                bool firstBundleWrapper = true;
                for (int i = 0; i < results.Count; i++)
                {
                    var abWrapper = results[i] as IAssetBundleResource;
                    if (abWrapper != null)
                    {
                        //only use the first asset bundle, even if it is invalid
                        abWrapper.GetAssetBundle();
                        if (firstBundleWrapper)
                            bundle = abWrapper;
                        firstBundleWrapper = false;
                    }
                }
                return bundle;
            }

            public void Start(ProvideHandle provideHandle)
            {
                provideHandle.SetProgressCallback(ProgressCallback);
                provideHandle.SetWaitForCompletionCallback(WaitForCompletionHandler);
                subObjectName = null;
                m_ProvideHandle = provideHandle;
                m_RequestOperation = null;
                List<object> deps = new List<object>(); // TODO: garbage. need to pass actual count and reuse the list
                m_ProvideHandle.GetDependencies(deps);
                var bundleResource = LoadBundleFromDependecies(deps);
                if (bundleResource == null)
                {
                    m_ProvideHandle.Complete<AssetBundle>(null, false, new Exception("Unable to load dependent bundle from location " + m_ProvideHandle.Location));
                }
                else
                {
                    var bundle = bundleResource.GetAssetBundle();
                    if (bundle == null)
                    {
                        m_ProvideHandle.Complete<AssetBundle>(null, false, new Exception("Unable to load dependent bundle from location " + m_ProvideHandle.Location));
                    }
                    else
                    {
                        var assetPath = m_ProvideHandle.ResourceManager.TransformInternalId(m_ProvideHandle.Location);
                        if (m_ProvideHandle.Type.IsArray)
                        {
#if !UNITY_2021_1_OR_NEWER
                            if (AsyncOperationHandle.IsWaitingForCompletion)
                            {
                                GetArrayResult(bundle.LoadAssetWithSubAssets(assetPath, m_ProvideHandle.Type.GetElementType()));
                                CompleteOperation();
                            }
                            else
#endif
                                m_RequestOperation = bundle.LoadAssetWithSubAssetsAsync(assetPath, m_ProvideHandle.Type.GetElementType());
                        }
                        else if (m_ProvideHandle.Type.IsGenericType && typeof(IList<>) == m_ProvideHandle.Type.GetGenericTypeDefinition())
                        {
#if !UNITY_2021_1_OR_NEWER
                            if (AsyncOperationHandle.IsWaitingForCompletion)
                            {
                                GetListResult(bundle.LoadAssetWithSubAssets(assetPath, m_ProvideHandle.Type.GetGenericArguments()[0]));
                                CompleteOperation();
                            }
                            else
#endif
                                m_RequestOperation = bundle.LoadAssetWithSubAssetsAsync(assetPath, m_ProvideHandle.Type.GetGenericArguments()[0]);
                        }
                        else
                        {
                            if (ResourceManagerConfig.ExtractKeyAndSubKey(assetPath, out string mainPath, out string subKey))
                            {
                                subObjectName = subKey;
#if !UNITY_2021_1_OR_NEWER
                                if (AsyncOperationHandle.IsWaitingForCompletion)
                                {
                                    GetAssetSubObjectResult(bundle.LoadAssetWithSubAssets(mainPath, m_ProvideHandle.Type));
                                    CompleteOperation();
                                }
                                else
#endif
                                    m_RequestOperation = bundle.LoadAssetWithSubAssetsAsync(mainPath, m_ProvideHandle.Type);
                            }
                            else
                            {
#if !UNITY_2021_1_OR_NEWER
                                if (AsyncOperationHandle.IsWaitingForCompletion)
                                {
                                    GetAssetResult(bundle.LoadAsset(assetPath, m_ProvideHandle.Type));
                                    CompleteOperation();
                                }
                                else
#endif
                                    m_RequestOperation = bundle.LoadAssetAsync(assetPath, m_ProvideHandle.Type);
                            }
                        }
                        
                        if (m_RequestOperation != null)
                        {
                            if (m_RequestOperation.isDone)
                                ActionComplete(m_RequestOperation);
                            else
                                m_RequestOperation.completed += ActionComplete;
                        }
                    }
                }
            }

            private bool WaitForCompletionHandler()
            {
                if (m_Result != null)
                    return true;
                if (m_RequestOperation == null)
                    return false;
                if (m_RequestOperation.isDone)
                    return true;
                return m_RequestOperation.asset != null;
            }
            
            private void ActionComplete(AsyncOperation obj)
            {
                if (m_RequestOperation != null)
                {
                    if (m_ProvideHandle.Type.IsArray)
                        GetArrayResult(m_RequestOperation.allAssets);
                    else if (m_ProvideHandle.Type.IsGenericType && typeof(IList<>) == m_ProvideHandle.Type.GetGenericTypeDefinition())
                        GetListResult(m_RequestOperation.allAssets);
                    else if (string.IsNullOrEmpty(subObjectName))
                        GetAssetResult(m_RequestOperation.asset);
                    else
                        GetAssetSubObjectResult(m_RequestOperation.allAssets);
                }
                CompleteOperation();
            }
            
            private void GetArrayResult(Object[] allAssets)
            {
                m_Result = ResourceManagerConfig.CreateArrayResult(m_ProvideHandle.Type, allAssets);
            }

            private void GetListResult(Object[] allAssets)
            {
                m_Result = ResourceManagerConfig.CreateListResult(m_ProvideHandle.Type, allAssets);
            }

            private void GetAssetResult(Object asset)
            {
                m_Result = (asset != null && m_ProvideHandle.Type.IsAssignableFrom(asset.GetType())) ? asset : null;
            }
            
            private void GetAssetSubObjectResult(Object[] allAssets)
            {
                foreach (var o in allAssets)
                {
                    if (o.name == subObjectName)
                    {
                        if (m_ProvideHandle.Type.IsAssignableFrom(o.GetType()))
                        {
                            m_Result = o;
                            break;
                        }
                    }
                }
            }
            
            void CompleteOperation()
            {
                Exception e = m_Result == null
                    ? new Exception($"Unable to load asset of type {m_ProvideHandle.Type} from location {m_ProvideHandle.Location}.")
                    : null;
                m_ProvideHandle.Complete(m_Result, m_Result != null, e);
            }

            public float ProgressCallback() { return m_RequestOperation != null ? m_RequestOperation.progress : 0.0f; }
        }

        /// <inheritdoc/>
        public override void Provide(ProvideHandle provideHandle)
        {
            new InternalOp().Start(provideHandle);
        }
    }
}
