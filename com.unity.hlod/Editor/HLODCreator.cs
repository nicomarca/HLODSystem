﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.Collections;
using Unity.HLODSystem.Serializer;
using Unity.HLODSystem.Simplifier;
using Unity.HLODSystem.SpaceManager;
using Unity.HLODSystem.Streaming;
using Unity.HLODSystem.Utils;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Unity.HLODSystem
{
    public static class HLODCreator
    {
        private static List<Collider> GetColliders(List<GameObject> gameObjects, float minObjectSize)
        {
            List<Collider> results = new List<Collider>();

            for (int i = 0; i < gameObjects.Count; ++i)
            {
                GameObject obj = gameObjects[i];
                Collider[] colliders = obj.GetComponentsInChildren<Collider>();
                
                for (int ci = 0; ci < colliders.Length; ++ci)
                {
                    Collider collider = colliders[ci];
                    float max = Mathf.Max(collider.bounds.size.x, collider.bounds.size.y, collider.bounds.size.z);
                    if (max < minObjectSize)
                        continue;
                    
                    results.Add(collider);
                }
            }

            return results;
        }

        private struct TravelQueueItem
        {
            public SpaceNode Node;
            public int Parent;
            public string Name;
            public int Level;
            public List<GameObject> TargetGameObjects;
            public List<int> Distances;
        }

        private static void CopyObjectsToParent(List<TravelQueueItem> list, int curIndex, List<GameObject> objects, int distance)
        {
            if (curIndex < 0)
                return;

            int parentIndex = list[curIndex].Parent;
            
            if (parentIndex < 0)
                return;

            var parent = list[parentIndex];

            parent.TargetGameObjects.AddRange(objects);
            parent.Distances.AddRange(Enumerable.Repeat<int>(distance, objects.Count));
            
            CopyObjectsToParent(list, parentIndex, objects, distance + 1);

        }
        private static DisposableList<HLODBuildInfo> CreateBuildInfo(HLOD hlod, SpaceNode root, float minObjectSize)
        {
            //List<HLODBuildInfo> resultsCandidates = new List<HLODBuildInfo>();
            
            Queue<TravelQueueItem> travelQueue = new Queue<TravelQueueItem>();
            
            List<TravelQueueItem> candidateItems = new List<TravelQueueItem>();
            List<HLODBuildInfo> buildInfoCandidates = new List<HLODBuildInfo>();
            
            int maxLevel = 0;
            
            travelQueue.Enqueue(new TravelQueueItem()
            {
                Node = root,
                Parent = -1,
                Level = 0,
                Name = "",
                TargetGameObjects = new List<GameObject>(),
                Distances = new List<int>(),
                
            });

            while (travelQueue.Count > 0)
            {
                int currentNodeIndex = candidateItems.Count;
                TravelQueueItem item = travelQueue.Dequeue();

                for (int i = 0; i < item.Node.GetChildCount(); ++i)
                {
                    travelQueue.Enqueue(new TravelQueueItem()
                    {
                        Node = item.Node.GetChild(i),
                        Parent = currentNodeIndex,
                        Level = item.Level + 1,
                        Name = item.Name + "_" + (i+1),
                        TargetGameObjects = new List<GameObject>(),
                        Distances = new List<int>(),
                    });
                }

                maxLevel = Math.Max(maxLevel, item.Level);
                candidateItems.Add(item);
                buildInfoCandidates.Add(new HLODBuildInfo()
                {
                    Name = item.Name,
                    ParentIndex = item.Parent,
                    Target = item.Node
                });
                item.TargetGameObjects.AddRange(item.Node.Objects);
                item.Distances.AddRange(Enumerable.Repeat<int>(0, item.Node.Objects.Count));

                CopyObjectsToParent(candidateItems, currentNodeIndex, item.Node.Objects, 1);
            }

            for (int i = 0; i < candidateItems.Count; ++i)
            {
                var info = buildInfoCandidates[i];
                var item = candidateItems[i];
                var level = maxLevel - item.Level;  //< It needs to be turned upside down. The terminal node must have level 0.
                var meshRenderers = new List<MeshRenderer>();
                var distances = new List<int>();
                var colliders = GetColliders(item.TargetGameObjects, minObjectSize);


                for (int ti = 0; ti < item.TargetGameObjects.Count; ++ti)
                {
                    var curRenderers = CreateUtils.GetMeshRenderers(item.TargetGameObjects[ti], minObjectSize, level);
                    var curDistance = item.Distances[ti];
                    
                    meshRenderers.AddRange(curRenderers);
                    distances.AddRange(Enumerable.Repeat<int>(curDistance, curRenderers.Count));
                }
                
                for (int mi = 0; mi < meshRenderers.Count; ++mi)
                {
                    info.WorkingObjects.Add(meshRenderers[mi].ToWorkingObject(Allocator.Persistent));
                    info.Distances.Add(distances[mi]);
                }

                for (int ci = 0; ci < colliders.Count; ++ci)
                {
                    info.Colliders.Add(colliders[ci].ToWorkingCollider(hlod));
                }
            }
            
            DisposableList<HLODBuildInfo> results = new DisposableList<HLODBuildInfo>();
            
            for (int i = 0; i < buildInfoCandidates.Count; ++i)
            {
                if (buildInfoCandidates[i].WorkingObjects.Count > 0)
                {
                    results.Add(buildInfoCandidates[i]);
                }
                else
                {
                    buildInfoCandidates[i].Dispose();
                }
            }
            
            return results;
        }

        public static IEnumerator Create(HLOD hlod)
        {
            try
            {
                Stopwatch sw = new Stopwatch();
                TimeSpan totalTime = new TimeSpan();

                AssetDatabase.Refresh();
                AssetDatabase.SaveAssets();

                totalTime = totalTime + sw.Elapsed;
                sw.Reset();
                sw.Start();
                
                hlod.ConvertedPrefabObjects.Clear();
                hlod.GeneratedObjects.Clear();

                Bounds bounds = hlod.GetBounds();

                List<GameObject> hlodTargets = ObjectUtils.HLODTargets(hlod.gameObject);
                ISpaceSplitter spliter = SpaceSplitterTypes.CreateInstance(hlod);
                if (spliter == null)
                {
                    EditorUtility.DisplayDialog("SpaceSplitter not found",
                        "There is no SpaceSplitter. Please set the SpaceSplitter.",
                        "OK");
                    yield break;
                    
                }
                List<SpaceNode> rootNodeList = spliter.CreateSpaceTree(bounds, hlod.ChunkSize, hlod.transform, hlodTargets, progress =>
                {
                    EditorUtility.DisplayProgressBar("Bake HLOD", "Splitting space", progress * 0.20f);
                });

                if (hlodTargets.Count == 0)
                {
                    EditorUtility.DisplayDialog("Empty HLOD sources.",
                        "There are no objects to be included in the HLOD.",
                        "Ok");
                    yield break;
                }

                if (rootNodeList.Count >= 256)
                {
                    EditorUtility.DisplayDialog("Too many SubHLODTrees.",
                        "There are too many SubHLODTrees. SubHLODtree is supported less than 256.",
                        "Ok");
                    yield break;
                }

                IBatcher batcher = (IBatcher)Activator.CreateInstance(hlod.BatcherType, new object[] { hlod.BatcherOptions });
                batcher.PreProcess(hlod.transform, progress =>
                {
                    EditorUtility.DisplayProgressBar("Bake HLOD", "Storing results.",
                        0.20f + progress * 0.05f);
                });

                for ( int ri = 0; ri < rootNodeList.Count; ++ ri)
                {
                    var rootNode = rootNodeList[ri];
                    
                    using (DisposableList<HLODBuildInfo> buildInfos =
                           CreateBuildInfo(hlod, rootNode, hlod.MinObjectSize))
                    {
                        if (buildInfos.Count == 0 || buildInfos[0].WorkingObjects.Count == 0)
                        {
                            continue;
                        }


                        Debug.Log("[HLOD] Splite space: " + sw.Elapsed.ToString("hh':'mm':'ss':'fff"));

                        totalTime = totalTime + sw.Elapsed;
                        sw.Reset();
                        sw.Start();

                        ISimplifier simplifier = (ISimplifier)Activator.CreateInstance(hlod.SimplifierType,
                            new object[] { hlod.SimplifierOptions });
                        for (int i = 0; i < buildInfos.Count; ++i)
                        {
                            yield return new BranchCoroutine(simplifier.Simplify(buildInfos[i]));
                        }

                        yield return new WaitForBranches(progress =>
                        {
                            EditorUtility.DisplayProgressBar("Bake HLOD", "Simplify meshes",
                                0.25f + progress * 0.25f);
                        });
                        Debug.Log("[HLOD] Simplify: " + sw.Elapsed.ToString("hh':'mm':'ss':'fff"));

                        totalTime = totalTime + sw.Elapsed;
                        sw.Reset();
                        sw.Start();

                        batcher.Batch(hlod.transform, buildInfos,
                            progress =>
                            {
                                EditorUtility.DisplayProgressBar("Bake HLOD", "Generating combined static meshes.",
                                    0.5f + progress * 0.25f);
                            });

                        Debug.Log("[HLOD] Batch: " + sw.Elapsed.ToString("hh':'mm':'ss':'fff"));

                        totalTime = totalTime + sw.Elapsed;
                        sw.Reset();
                        sw.Start();

                        GameObject targetGameObject = hlod.gameObject;
                        //If there are more than 1 rootNode, the HLOD use sub tree.
                        //So we should separate GameObject to generate Streaming component.
                        if (rootNodeList.Count > 1)
                        {
                            GameObject newTargetGameObject = new GameObject($"{targetGameObject.name}_SubTree{ri}");
                            newTargetGameObject.transform.SetParent(targetGameObject.transform, false);
                            hlod.AddGeneratedResource(newTargetGameObject);

                            targetGameObject = newTargetGameObject;
                        }

                        IStreamingBuilder builder =
                            (IStreamingBuilder)Activator.CreateInstance(hlod.StreamingType,
                                new object[] { hlod, ri, hlod.StreamingOptions });
                        builder.Build(rootNode, buildInfos, targetGameObject, hlod.CullDistance, hlod.LODDistance, false,
                            true,
                            progress =>
                            {
                                EditorUtility.DisplayProgressBar("Bake HLOD", "Storing results.",
                                    0.75f + progress * 0.25f);
                            });
                        Debug.Log("[HLOD] Build: " + sw.Elapsed.ToString("hh':'mm':'ss':'fff"));

                        totalTime = totalTime + sw.Elapsed;
                        Debug.Log("Total time elapsed: " + totalTime.ToString("hh':'mm':'ss':'fff"));
                        sw.Reset();
                        sw.Start();
                    }
                }

                batcher.Dispose();

                UserDataSerialization(hlod);
                EditorUtility.SetDirty(hlod);
                EditorUtility.SetDirty(hlod.gameObject);

            }
            finally
            {
                EditorUtility.ClearProgressBar();
                
            }
            
        }

        public static IEnumerator Destroy(HLOD hlod)
        {

            if (hlod.GeneratedObjects.Count == 0)
                yield break;

            try
            {
                EditorUtility.DisplayProgressBar("Destroy HLOD", "Destrying HLOD files", 0.0f);
                var convertedPrefabObjects = hlod.ConvertedPrefabObjects;
                for (int i = 0; i < convertedPrefabObjects.Count; ++i)
                {
                    PrefabUtility.UnpackPrefabInstance(convertedPrefabObjects[i], PrefabUnpackMode.OutermostRoot,
                        InteractionMode.AutomatedAction);
                }

                var generatedObjects = hlod.GeneratedObjects;
                for (int i = 0; i < generatedObjects.Count; ++i)
                {
                    if (generatedObjects[i] == null)
                        continue;
                    var path = AssetDatabase.GetAssetPath(generatedObjects[i]);
                    if (string.IsNullOrEmpty(path) == false)
                    {
                        AssetDatabase.DeleteAsset(path);
                    }
                    else
                    {
                        //It means scene object.
                        //destory it.
                        Object.DestroyImmediate(generatedObjects[i]);
                    }

                    EditorUtility.DisplayProgressBar("Destroy HLOD", "Destrying HLOD files", (float)i / (float)generatedObjects.Count);
                }
                generatedObjects.Clear();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            
            EditorUtility.SetDirty(hlod.gameObject);
            EditorUtility.SetDirty(hlod);
        }


        private static void UserDataSerialization(HLOD hlod)
        {
            var serializer = hlod.gameObject.AddComponent(hlod.UserDataSerializerType) as UserDataSerializerBase;

            if (serializer == null)
                return;
            
            hlod.AddGeneratedResource(serializer);

            var controllers = hlod.GetHLODControllerBases();
            if (controllers.Count == 0)
                 return;

            foreach (var controller in controllers)
            {
                controller.UserDataserializer = serializer;
                for (int i = 0; i < controller.HighObjectCount; ++i)
                {
                    var obj = controller.GetHighSceneObject(i);
                    serializer.SerializeUserData(controller, i, obj);
                }
            }
        }

    }
}
