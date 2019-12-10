﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.HLODSystem.Streaming;
using Unity.HLODSystem.Utils;
using UnityEditor;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Unity.HLODSystem.RuntimeTests
{
    [TestFixture]
    public class RuntimeTests : IPrebuildSetup, IPostBuildCleanup
    {
        private GameObject mGameObject;
        private GameObject mHlodGameObject;
        private GameObject mHlodCameraObject;

        [SetUp]
        public void Setup()
        {
            mGameObject =
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TestAssets/Prefabs/HLODTestPrefabBaked.prefab");
            mGameObject = GameObject.Instantiate(mGameObject, Vector3.zero, Quaternion.identity);

            new WaitForSeconds(0.1f);

            Assert.NotNull(mGameObject);

            mHlodGameObject = mGameObject.transform.Find("HLOD").gameObject;
            Assert.NotNull(mHlodGameObject);

            mHlodGameObject.GetComponentInChildren<HLODControllerBase>().Install();

            mHlodCameraObject = mGameObject.transform.Find("HLOD Camera").gameObject;
            Assert.NotNull(mHlodCameraObject);
        }

        public void Cleanup()
        {
            Object.Destroy(mHlodCameraObject);
            Object.Destroy(mHlodGameObject);
            Object.Destroy(mGameObject);
        }

        [Test]
        public void HlodGameObjectHasChildren()
        {
            int childrenCount = mHlodGameObject.transform.childCount;
            Assert.True(childrenCount == 11);
        }

        [Test]
        public void CameraHasHlodCameraRecognizerComponent()
        {
            HLODCameraRecognizer hlodCameraRecognizer = mHlodCameraObject.GetComponent<HLODCameraRecognizer>();
            Assert.NotNull(hlodCameraRecognizer);
        }

        [UnityTest]
        public IEnumerator HlodKicksInForFurtherObjects()
        {
            TestData testData = TestData.CreateFromJson("Assets/TestAssets/RawTestData/TestData_1.json");

            SetUpCamera(testData.cameraSettings);

            yield return new WaitForSeconds(0.1f);

            CheckGameObjectActiveState(testData.listOfGameObjects);
            CheckHlodObjectsActiveState(testData.listOfActiveHlods);

            yield return null;
        }

        [UnityTest]
        public IEnumerator HlodNotKickedInAtAllForCloseObjects()
        {
            TestData testData = TestData.CreateFromJson("Assets/TestAssets/RawTestData/TestData_2.json");

            SetUpCamera(testData.cameraSettings);

            yield return new WaitForSeconds(0.1f);

            CheckGameObjectActiveState(testData.listOfGameObjects);
            CheckHlodObjectsActiveState(testData.listOfActiveHlods);

            yield return null;
        }

        [UnityTest]
        public IEnumerator HlodKicksInForFurtherObjects_2()
        {
            TestData testData = TestData.CreateFromJson("Assets/TestAssets/RawTestData/TestData_3.json");

            SetUpCamera(testData.cameraSettings);

            yield return new WaitForSeconds(0.1f);

            CheckGameObjectActiveState(testData.listOfGameObjects);
            CheckHlodObjectsActiveState(testData.listOfActiveHlods);

            yield return null;
        }

        [UnityTest]
        public IEnumerator HlodFullyKicksIn()
        {
            TestData testData = TestData.CreateFromJson("Assets/TestAssets/RawTestData/TestData_4.json");

            SetUpCamera(testData.cameraSettings);

            yield return new WaitForSeconds(0.1f);

            CheckGameObjectActiveState(testData.listOfGameObjects);
            CheckHlodObjectsActiveState(testData.listOfActiveHlods);

            yield return null;
        }

        [UnityTest]
        public IEnumerator HlodNotKickedInAtAll()
        {
            TestData testData = TestData.CreateFromJson("Assets/TestAssets/RawTestData/TestData_5.json");

            SetUpCamera(testData.cameraSettings);

            yield return new WaitForSeconds(0.1f);

            CheckGameObjectActiveState(testData.listOfGameObjects);
            CheckHlodObjectsActiveState(testData.listOfActiveHlods);

            yield return null;
        }

        private void SetUpCamera(CameraSettings cameraSettings)
        {
            Camera hlodCamera = mHlodCameraObject.GetComponent<Camera>();

            hlodCamera.transform.position = new Vector3(
                cameraSettings.location.x,
                cameraSettings.location.y,
                cameraSettings.location.z);

            hlodCamera.transform.eulerAngles = new Vector3(
                cameraSettings.rotation.x,
                cameraSettings.rotation.y + 180,
                cameraSettings.rotation.z);

            HLODManager.Instance.OnPreCull(hlodCamera);
        }

        private void CheckGameObjectActiveState(List<PlayModeTestGameObject> listOfGameObjects)
        {
            foreach (PlayModeTestGameObject playModeTestGameObject in listOfGameObjects)
            {
                Transform rinNumbers = mHlodGameObject.transform.Find(playModeTestGameObject.groupName);

                for (int i = 0; i < rinNumbers.childCount; i++)
                    Assert.AreEqual(rinNumbers.GetChild(i).gameObject.activeSelf, playModeTestGameObject.enabled[i]);
            }
        }

        private void CheckHlodObjectsActiveState(List<string> listOfActiveHlods)
        {
            HashSet<string> hashSet = new HashSet<string>(listOfActiveHlods);

            Transform hlods = mHlodGameObject.transform.Find("HLODRoot");

            string fig = "";

            foreach (Transform child in hlods.transform)
            {
                if (child.gameObject.activeSelf)
                    fig += "\"" + child.gameObject.name + "\", ";
            }

            foreach (Transform child in hlods.transform)
                Assert.AreEqual(child.gameObject.activeSelf, hashSet.Contains(child.gameObject.name));
        }
    }
    
    [Serializable]
    public class TestData
    {
        public CameraSettings cameraSettings;
        public List<PlayModeTestGameObject> listOfGameObjects;
        public List<string> listOfActiveHlods;

        public static TestData CreateFromJson(string jsonFilePath)
        {
            if (!File.Exists(jsonFilePath))
                throw new FileNotFoundException();

            string dataAsJson = File.ReadAllText(jsonFilePath);

            return JsonUtility.FromJson<TestData>(dataAsJson);
        }
    }

    [Serializable]
    public class CustomVector3
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public class PlayModeTestGameObject
    {
        public string groupName;
        public bool[] enabled;
    }

    [Serializable]
    public class CameraSettings
    {
        public CustomVector3 location;
        public CustomVector3 rotation;
    }
}