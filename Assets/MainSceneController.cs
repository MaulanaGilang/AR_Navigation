using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Google.XR.ARCoreExtensions;
using Google.XR.ARCoreExtensions.Samples.Geospatial;
using TMPro;
using UnityEngine.Networking;
using Unity.VisualScripting;
using System.Linq;



#if UNITY_ANDROID

using UnityEngine.Android;
#endif

public class MainSceneController : MonoBehaviour
{
    [Header("AR Components")]

    public ARSessionOrigin SessionOrigin;

    public ARSession Session;

    public ARAnchorManager AnchorManager;

    public ARRaycastManager RaycastManager;

    public AREarthManager EarthManager;

    public ARCoreExtensions ARCoreExtensions;

    [Header("UI Elements")]

    public GameObject SelectDestinationCanvas;

    public GameObject VPSCheckCanvas;

    public GameObject ARViewCanvas;

    public GameObject EnableCameraCanvas;

    public Text SnackBarText;

    public Text DebugText;

    public GameObject ArrowPrefab;
    public GameObject FinishPrefab;

    public TMP_Dropdown LocationDropdown;


    private const string _localizingMessage = "Localizing your device to set anchor.";

    private const string _localizationInitializingMessage =
        "Initializing Geospatial functionalities.";

    private const string _localizationInstructionMessage =
        "Point your camera at buildings, stores, and signs near you.";

    private const string _localizationFailureMessage =
        "Localization not possible.\n" +
        "Close and open the app to restart the session.";

    private const string _localizationSuccessMessage = "Localization completed.";

    private const float _timeoutSeconds = 180;

    private const float _errorDisplaySeconds = 3;

    private const string _hasDisplayedPrivacyPromptKey = "HasDisplayedGeospatialPrivacyPrompt";

    private const string _persistentGeospatialAnchorsStorageKey = "PersistentGeospatialAnchors";

    private const int _storageLimit = 20;

    private const double _orientationYawAccuracyThreshold = 25;

    private const double _headingAccuracyThreshold = 25;
    private const double _horizontalAccuracyThreshold = 20;

    private bool _showAnchorSettingsPanel = false;

    private AnchorType _anchorType = AnchorType.Geospatial;

    private bool _streetscapeGeometryVisibility = false;

    private Dictionary<TrackableId, GameObject> _streetscapegeometryGOs =
        new Dictionary<TrackableId, GameObject>();

    List<ARStreetscapeGeometry> _addedStreetscapeGeometries =
        new List<ARStreetscapeGeometry>();

    List<ARStreetscapeGeometry> _updatedStreetscapeGeometries =
        new List<ARStreetscapeGeometry>();

    List<ARStreetscapeGeometry> _removedStreetscapeGeometries =
        new List<ARStreetscapeGeometry>();

    private bool _clearStreetscapeGeometryRenderObjects = false;

    private bool _waitingForLocationService = false;
    private bool _isInARView = false;
    private bool _isReturning = false;
    private bool _isLocalizing = false;
    private bool _enablingGeospatial = false;
    private float _localizationPassedTime = 0f;
    private float _configurePrepareTime = 3f;
    private SortedDictionary<int, GameObject> _anchorObjects = new SortedDictionary<int, GameObject>();
    private SortedDictionary<int, GameObject> _arrowObjects = new SortedDictionary<int, GameObject>();
    private IEnumerator _startLocationService = null;
    private IEnumerator _asyncCheck = null;
    private bool _needToPlaceAnchor = false;
    private bool _alreadyRotated = false;
    private NodeList _nodes = null;

    private List<Node> _nodesMock = new()
            {
                    new Node
                    {
                        id = 1,
                        places_name = "Lokasi Anda",
                        latitude = -7.286963908273377,
                        longitude = 112.79832451532494,
                        total_nodes = 0
                    },
                    new Node
                    {
                        id = 2,
                        places_name = "Lokasi 1",
                        latitude = -7.286830880990744,
                        longitude = 112.79836474849103,
                        total_nodes = 0
                    },
                    new Node
                    {
                        id = 3,
                        places_name = "Lokasi 2",
                        latitude = -7.286784321391746,
                        longitude = 112.79825880126684,
                        total_nodes = 0
                    },
                    new Node
                    {
                        id = 4,
                        places_name = "Lokasi 3",
                        latitude = -7.286841523181405,
                        longitude = 112.79782160115107,
                        total_nodes = 0
                    },
                };


    public void OnStartNavigationClicked()
    {
        var destinationId = LocationDropdown.GetComponent<DropdownPopulator>().GetSelectedPlaceId();

        Debug.Log("Selected location: " + destinationId);

        if (destinationId == -1)
        {
            Debug.LogError("Invalid dropdown index!");
            // Toast.Show("Pilih lokasi terlebih dahulu");
            return;
        }

        var location = Input.location.lastData;
        var latitude = location.latitude;
        var longitude = location.longitude;

        Debug.Log($"Current location: {latitude}, {longitude}");

        StartCoroutine(CallAPI(latitude, longitude, destinationId));
    }

    private IEnumerator CallAPI(double latitude, double longitude, int destinationId)
    {
        string apiURL = "https://backend-protel-nasdem.vercel.app/api/route";
        apiURL += $"?latitude={latitude}&longitude={longitude}&endId={destinationId}";

        using var request = UnityWebRequest.Get(apiURL);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
        {
            var jsonResponse = request.downloadHandler.text;
            var error = JsonUtility.FromJson<Error>(jsonResponse);
            // Toast.Show(error.error);
            Debug.LogError($"Error calling API: {request.error}");
        }
        else
        {
            var jsonResponse = request.downloadHandler.text;
            // Debug.Log($"Response: {jsonResponse}");
            _nodes = JsonUtility.FromJson<NodeList>(jsonResponse);
            _needToPlaceAnchor = true;

            ShowChooseDestination(false);
            EnableCameraCanvas.SetActive(true);
        }

    }

    private IEnumerator PlaceAnchor(Node node, int order, bool isDestination = false)
    {
        ResolveAnchorOnTerrainPromise promise = AnchorManager.ResolveAnchorOnTerrainAsync(node.latitude, node.longitude, isDestination ? 0f : 0.5f, Quaternion.identity);

        yield return promise;

        var result = promise.Result;

        if (result.TerrainAnchorState == TerrainAnchorState.Success && result.Anchor != null)
        {
            Debug.Log($"Anchor resolved successfully. {node.id}");

            GameObject arrowInstance = Instantiate(isDestination ? FinishPrefab : ArrowPrefab, result.Anchor.transform.position, Quaternion.identity);

            arrowInstance.transform.parent = result.Anchor.gameObject.transform;

            _anchorObjects.Add(order, result.Anchor.gameObject);
            _arrowObjects.Add(order, arrowInstance);
        }
        else
        {
            Debug.Log("Anchor resolution failed.");
        }

        yield return null;
    }

    private void ShowChooseDestination(bool show)
    {
        SelectDestinationCanvas.SetActive(show);
    }

    public void OnContinueClicked()
    {
        VPSCheckCanvas.SetActive(false);
    }

    public void OnGeometryToggled(bool enabled)
    {
        _streetscapeGeometryVisibility = enabled;
        if (!_streetscapeGeometryVisibility)
        {
            _clearStreetscapeGeometryRenderObjects = true;
        }
    }

    public void Awake()
    {
        Screen.autorotateToLandscapeLeft = false;
        Screen.autorotateToLandscapeRight = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.orientation = ScreenOrientation.Portrait;

        Application.targetFrameRate = 60;

        if (SessionOrigin == null)
        {
            Debug.LogError("Cannot find ARSessionOrigin.");
        }

        if (Session == null)
        {
            Debug.LogError("Cannot find ARSession.");
        }

        if (ARCoreExtensions == null)
        {
            Debug.LogError("Cannot find ARCoreExtensions.");
        }
    }

    public void OnEnable()
    {
        _startLocationService = StartLocationService();
        StartCoroutine(_startLocationService);

        _isReturning = false;
        _enablingGeospatial = false;
        DebugText.gameObject.SetActive(Debug.isDebugBuild && EarthManager != null);

        _localizationPassedTime = 0f;
        _isLocalizing = true;
        SnackBarText.text = _localizingMessage;
    }

    public void OnEnableCameraClicked()
    {
        EnableCameraCanvas.SetActive(false);
        SwitchToARView(true);
    }

    public void OnDisable()
    {
        StopCoroutine(_asyncCheck);
        _asyncCheck = null;
        StopCoroutine(_startLocationService);
        _startLocationService = null;
        Debug.Log("Stop location services.");
        Input.location.Stop();

        foreach (var anchor in _anchorObjects)
        {
            Destroy(anchor.Value);
        }

        _anchorObjects.Clear();
    }

    public void Update()
    {
        if (!_isInARView)
        {
            return;
        }

        UpdateDebugInfo();

        // Check session error status.
        LifecycleUpdate();
        if (_isReturning)
        {
            return;
        }

        if (ARSession.state != ARSessionState.SessionInitializing &&
            ARSession.state != ARSessionState.SessionTracking)
        {
            return;
        }

        // Check feature support and enable Geospatial API when it's supported.
        var featureSupport = EarthManager.IsGeospatialModeSupported(GeospatialMode.Enabled);
        switch (featureSupport)
        {
            case FeatureSupported.Unknown:
                return;
            case FeatureSupported.Unsupported:
                ReturnWithReason("The Geospatial API is not supported by this device.");
                return;
            case FeatureSupported.Supported:
                if (ARCoreExtensions.ARCoreExtensionsConfig.GeospatialMode ==
                    GeospatialMode.Disabled)
                {
                    Debug.Log("Geospatial sample switched to GeospatialMode.Enabled.");
                    ARCoreExtensions.ARCoreExtensionsConfig.GeospatialMode =
                        GeospatialMode.Enabled;
                    ARCoreExtensions.ARCoreExtensionsConfig.StreetscapeGeometryMode =
                        StreetscapeGeometryMode.Enabled;
                    _configurePrepareTime = 3.0f;
                    _enablingGeospatial = true;
                    return;
                }

                break;
        }

        // Waiting for new configuration to take effect.
        if (_enablingGeospatial)
        {
            _configurePrepareTime -= Time.deltaTime;
            if (_configurePrepareTime < 0)
            {
                _enablingGeospatial = false;
            }
            else
            {
                return;
            }
        }

        // Check earth state.
        var earthState = EarthManager.EarthState;
        if (earthState == EarthState.ErrorEarthNotReady)
        {
            SnackBarText.text = _localizationInitializingMessage;
            return;
        }
        else if (earthState != EarthState.Enabled)
        {
            string errorMessage =
                "Geospatial sample encountered an EarthState error: " + earthState;
            Debug.LogWarning(errorMessage);
            SnackBarText.text = errorMessage;
            return;
        }

        // Check earth localization.
        bool isSessionReady = ARSession.state == ARSessionState.SessionTracking &&
            Input.location.status == LocationServiceStatus.Running;
        var earthTrackingState = EarthManager.EarthTrackingState;
        var pose = earthTrackingState == TrackingState.Tracking ?
            EarthManager.CameraGeospatialPose : new GeospatialPose();
        if (!isSessionReady || earthTrackingState != TrackingState.Tracking ||
            pose.OrientationYawAccuracy > _orientationYawAccuracyThreshold ||
            pose.HorizontalAccuracy > _horizontalAccuracyThreshold)
        {
            // Lost localization during the session.
            if (!_isLocalizing)
            {
                _isLocalizing = true;
                _localizationPassedTime = 0f;
                foreach (var go in _anchorObjects)
                {
                    go.Value.SetActive(false);
                }
            }

            if (_localizationPassedTime > _timeoutSeconds)
            {
                Debug.LogError("Geospatial sample localization timed out.");
                ReturnWithReason(_localizationFailureMessage);
            }
            else
            {
                _localizationPassedTime += Time.deltaTime;
                SnackBarText.text = _localizationInstructionMessage;
            }
        }
        else if (_isLocalizing)
        {
            // Finished localization.
            _isLocalizing = false;
            _localizationPassedTime = 0f;
            SnackBarText.text = _localizationSuccessMessage;

            if (_needToPlaceAnchor)
            {
                _needToPlaceAnchor = false;
                _alreadyRotated = false;

                var nodes = _nodes.nodes;

                for (int i = 0; i < nodes.Length; i++)
                {
                    StartCoroutine(PlaceAnchor(nodes[i], i, i == nodes.Length - 1));
                }
            }

            foreach (var go in _anchorObjects)
            {
                go.Value.SetActive(true);
            }
        }

        Debug.Log("Nodes length: " + _nodes.nodes.Length);
        Debug.Log("Arrow objects count: " + _arrowObjects.Count);
        Debug.Log("Anchor objects count: " + _anchorObjects.Count);

        if (_nodes.nodes.Length == _arrowObjects.Count && !_alreadyRotated)
        {
            Debug.Log("Rotating arrows");
            for (int i = 0; i < _arrowObjects.Count - 1; i++)
            {
                _arrowObjects[i].transform.LookAt(_arrowObjects[i + 1].transform, Vector3.up);
                _arrowObjects[i].transform.rotation = Quaternion.Euler(0, _arrowObjects[i].transform.rotation.eulerAngles.y + 90, 0);
            }

            _alreadyRotated = true;
        }
    }

    private void SwitchToARView(bool enable)
    {
        _isInARView = enable;
        SessionOrigin.gameObject.SetActive(enable);
        Session.gameObject.SetActive(enable);
        ARCoreExtensions.gameObject.SetActive(enable);
        ARViewCanvas.SetActive(enable);
        VPSCheckCanvas.SetActive(false);
        if (enable && _asyncCheck == null)
        {
            _asyncCheck = AvailabilityCheck();
            StartCoroutine(_asyncCheck);
        }
    }

    private IEnumerator AvailabilityCheck()
    {
        if (ARSession.state == ARSessionState.None)
        {
            yield return ARSession.CheckAvailability();
        }

        // Waiting for ARSessionState.CheckingAvailability.
        yield return null;

        if (ARSession.state == ARSessionState.NeedsInstall)
        {
            yield return ARSession.Install();
        }

        // Waiting for ARSessionState.Installing.
        yield return null;
#if UNITY_ANDROID

        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Debug.Log("Requesting camera permission.");
            Permission.RequestUserPermission(Permission.Camera);
            yield return new WaitForSeconds(3.0f);
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            // User has denied the request.
            Debug.LogWarning(
                "Failed to get the camera permission. VPS availability check isn't available.");
            yield break;
        }
#endif

        while (_waitingForLocationService)
        {
            yield return null;
        }

        if (Input.location.status != LocationServiceStatus.Running)
        {
            Debug.LogWarning(
                "Location services aren't running. VPS availability check is not available.");
            yield break;
        }

        // Update event is executed before coroutines so it checks the latest error states.
        if (_isReturning)
        {
            yield break;
        }

        var location = Input.location.lastData;
        var vpsAvailabilityPromise =
            AREarthManager.CheckVpsAvailabilityAsync(location.latitude, location.longitude);
        yield return vpsAvailabilityPromise;

        Debug.LogFormat("VPS Availability at ({0}, {1}): {2}",
            location.latitude, location.longitude, vpsAvailabilityPromise.Result);
        //VPSCheckCanvas.SetActive(vpsAvailabilityPromise.Result != VpsAvailability.Available);
    }

    private IEnumerator StartLocationService()
    {
        _waitingForLocationService = true;
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Debug.Log("Requesting the fine location permission.");
            Permission.RequestUserPermission(Permission.FineLocation);
            yield return new WaitForSeconds(3.0f);
        }
#endif

        if (!Input.location.isEnabledByUser)
        {
            Debug.Log("Location service is disabled by the user.");
            _waitingForLocationService = false;
            yield break;
        }

        Debug.Log("Starting location service.");
        Input.location.Start();

        while (Input.location.status == LocationServiceStatus.Initializing)
        {
            yield return null;
        }

        _waitingForLocationService = false;
        if (Input.location.status != LocationServiceStatus.Running)
        {
            Debug.LogWarningFormat(
                "Location service ended with {0} status.", Input.location.status);
            Input.location.Stop();
        }
    }

    private void LifecycleUpdate()
    {
        if (Input.GetKey(KeyCode.Escape) && _isInARView)
        {
            ARViewCanvas.SetActive(false);
            _isInARView = false;
            ShowChooseDestination(true);
            _nodes = null;
            _needToPlaceAnchor = false;

            foreach (var anchor in _anchorObjects)
            {
                Destroy(anchor.Value);
            }

            _anchorObjects.Clear();
        }
        else if (Input.GetKey(KeyCode.Escape) && !_isInARView)
        {
            QuitApplication();
        }

        if (_isReturning)
        {
            return;
        }

        // Only allow the screen to sleep when not tracking.
        var sleepTimeout = SleepTimeout.NeverSleep;
        if (ARSession.state != ARSessionState.SessionTracking)
        {
            sleepTimeout = SleepTimeout.SystemSetting;
        }

        Screen.sleepTimeout = sleepTimeout;

        // Quit the app if ARSession is in an error status.
        string returningReason = string.Empty;
        if (ARSession.state != ARSessionState.CheckingAvailability &&
            ARSession.state != ARSessionState.Ready &&
            ARSession.state != ARSessionState.SessionInitializing &&
            ARSession.state != ARSessionState.SessionTracking)
        {
            returningReason = string.Format(
                "Geospatial sample encountered an ARSession error state {0}.\n" +
                "Please restart the app.",
                ARSession.state);
        }
        else if (Input.location.status == LocationServiceStatus.Failed)
        {
            returningReason =
                "Geospatial sample failed to start location service.\n" +
                "Please restart the app and grant the fine location permission.";
        }
        else if (SessionOrigin == null || Session == null || ARCoreExtensions == null)
        {
            returningReason = string.Format(
                "Geospatial sample failed due to missing AR Components.");
        }

        ReturnWithReason(returningReason);
    }

    private void ReturnWithReason(string reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return;
        }

        Debug.LogError(reason);
        SnackBarText.text = reason;
        _isReturning = true;
        Invoke(nameof(QuitApplication), _errorDisplaySeconds);
    }

    private void QuitApplication()
    {
        Application.Quit();
    }

    private void UpdateDebugInfo()
    {
        if (!Debug.isDebugBuild || EarthManager == null)
        {
            return;
        }

        var pose = EarthManager.EarthState == EarthState.Enabled &&
            EarthManager.EarthTrackingState == TrackingState.Tracking ?
            EarthManager.CameraGeospatialPose : new GeospatialPose();
        var supported = EarthManager.IsGeospatialModeSupported(GeospatialMode.Enabled);
        DebugText.text =
            $"IsReturning: {_isReturning}\n" +
            $"IsLocalizing: {_isLocalizing}\n" +
            $"SessionState: {ARSession.state}\n" +
            $"LocationServiceStatus: {Input.location.status}\n" +
            $"FeatureSupported: {supported}\n" +
            $"EarthState: {EarthManager.EarthState}\n" +
            $"EarthTrackingState: {EarthManager.EarthTrackingState}\n" +
            $"  LAT/LNG: {pose.Latitude:F6}, {pose.Longitude:F6}\n" +
            $"  HorizontalAcc: {pose.HorizontalAccuracy:F6}\n" +
            $"  ALT: {pose.Altitude:F2}\n" +
            $"  VerticalAcc: {pose.VerticalAccuracy:F2}\n" +
            $". EunRotation: {pose.EunRotation:F2}\n" +
            $"  OrientationYawAcc: {pose.OrientationYawAccuracy:F2}";
    }

    [System.Serializable]
    private class Node
    {
        public int id;
        public string places_name;
        public double latitude;
        public double longitude;
        public int total_nodes;
    }

    [System.Serializable]
    private class NodeList
    {
        public Node[] nodes;
    }

    [System.Serializable]
    private class Error
    {
        public string error;
    }
}
