using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnkrSDK.Metadata;
using AnkrSDK.WalletConnectSharp.Core;
using AnkrSDK.WalletConnectSharp.Core.Models;
using AnkrSDK.WalletConnectSharp.Core.Network;
using AnkrSDK.WalletConnectSharp.Unity.Models.DeepLink;
using AnkrSDK.WalletConnectSharp.Unity.Models.DeepLink.Helpers;
using AnkrSDK.WalletConnectSharp.Unity.Network;
using AnkrSDK.WalletConnectSharp.Unity.Utils;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Logger = AnkrSDK.InternalUtils.Logger;

namespace AnkrSDK.WalletConnectSharp.Unity
{
	[RequireComponent(typeof(NativeWebSocketTransport))]
	public class WalletConnect : MonoBehaviour
	{
		[SerializeField] private Wallets _defaultWallet = Wallets.MetaMask;
		[SerializeField] private bool _autoSaveAndResume = true;
		[SerializeField] private bool _connectOnAwake;
		[SerializeField] private bool _connectOnStart = true;
		[SerializeField] private bool _createNewSessionOnSessionDisconnect = true;
		[SerializeField] private int _connectSessionRetryCount = 3;
		[SerializeField] private string _customBridgeUrl = "https://testbridge.yartu.io/";
		[SerializeField] private int _chainId = 1;
		[SerializeField] private ClientMeta _appData = new ClientMeta();
		[SerializeField] private NativeWebSocketTransport _transport;

		[SerializeField] private WalletConnectEventWithSessionData _connectedEventSession;
		[SerializeField] private WalletConnectEventWithSession _disconnectedEvent;
		[SerializeField] private WalletConnectEventWithSession _newSessionConnected;
		[SerializeField] private WalletConnectEventWithSession _resumedSessionConnected;

		public event Action ConnectionStarted;
		public event Action SessionUpdated;

		private AppEntry SelectedWallet { get; set; }

		public string ConnectURL => Session.URI;

		private WalletConnectUnitySession _session;

		public WalletConnectUnitySession Session
		{
			get => _session;
			private set
			{
				Debug.Log("Active Session Changed");
				_session = value;
				SessionUpdated?.Invoke();
			}
		}

		public bool ConnectOnStart
		{
			set => _connectOnStart = value;
		}

		public bool ConnectOnAwake
		{
			set => _connectOnAwake = value;
		}

		public ClientMeta AppData
		{
			set => _appData = value;
		}

		public WalletConnectEventWithSessionData ConnectedEvent => _connectedEventSession;

	#if UNITY_EDITOR
		public NativeWebSocketTransport Transport
		{
			set => _transport = value;
		}
	#endif
		private static WalletConnect Instance { get; set; }

		private async void Awake()
		{
			EnsureTransportSetup();
		#if UNITY_WEBGL && !UNITY_EDITOR
			gameObject.SetActive(false);
			return;
		#endif
			if (Instance != null)
			{
				Destroy(gameObject);
				return;
			}

			DontDestroyOnLoad(gameObject);

			Instance = this;

			if (_connectOnAwake)
			{
				await Connect();
			}
		}

		private void EnsureTransportSetup()
		{
			if (_transport != null)
			{
				return;
			}

			_transport = GetComponent<NativeWebSocketTransport>();
			if (_transport == null)
			{
				throw new NullReferenceException("Couldn't find transport to setup");
			}
		}

		private async void Start()
		{
			if (_connectOnStart && !_connectOnAwake)
			{
				await Connect();
			}
		}

		private async void OnDestroy()
		{
			await SaveOrDisconnect();
		}

		private async void OnApplicationPause(bool pauseStatus)
		{
			if (pauseStatus)
			{
				await SaveOrDisconnect();
			}
			else if (SessionSaveHandler.IsSessionSaved() && _autoSaveAndResume)
			{
				await Connect();
			}
		}

		private async void OnApplicationQuit()
		{
			await SaveOrDisconnect();
		}

		public async Task<WCSessionData> Connect()
		{
			TeardownEvents();
			var savedSession = SessionSaveHandler.GetSavedSession();
			Logger.AddLog(PackageInfo.Version);

			if (string.IsNullOrWhiteSpace(_customBridgeUrl))
			{
				_customBridgeUrl = null;
			}

			if (Session != null)
			{
				if (savedSession != null)
				{
					if (Session.KeyData != savedSession.Key)
					{
						if (Session.Connected)
						{
							await Session.Disconnect();
						}
						else if (Session.TransportConnected)
						{
							await Session.Transport.Close();
						}
					}
					else if (!Session.Connected && !Session.Connecting)
					{
						if (!Session.Disconnected)
						{
							return await CompleteConnect();
						}
					}
					else
					{
						Debug.Log("Nothing to do, we are already connected and session key did not change");
						return null;
					}
				}
				else if (Session.Connected)
				{
					Debug.Log("We have old session connected, but no saved session. Disconnecting.");
					await Session.Disconnect();
				}
				else if (Session.TransportConnected)
				{
					Debug.Log("We have transport connected, but no saved session. Closing Transport.");
					await Session.Transport.Close();
				}
				else if (Session.Connecting)
				{
					Debug.Log("Session connection is in progress. Connect request ignored.");
					return null;
				}
			}

			InitializeUnitySession(savedSession);

			return await CompleteConnect();
		}

		public void InitializeUnitySession(SavedSession savedSession = null, ICipher cipher = null)
		{
			Session = savedSession != null
				? WalletConnectUnitySession.RestoreWalletConnectSession(savedSession, this, _transport)
				: WalletConnectUnitySession.GetNewWalletConnectSession(_appData, this, _customBridgeUrl, _transport,
					cipher, _chainId);
		}

		public void OpenMobileWallet(AppEntry selectedWallet)
		{
			SelectedWallet = selectedWallet;

			OpenMobileWallet();
		}

		public void OpenDeepLink(AppEntry selectedWallet)
		{
			SelectedWallet = selectedWallet;

			OpenDeepLink();
		}

		public void OpenMobileWallet()
		{
		#if UNITY_ANDROID
			var signingURL = ConnectURL.Split('@')[0];

			Application.OpenURL(signingURL);
		#elif UNITY_IOS
			if (SelectedWallet == null)
			{
				throw new NotImplementedException(
					"You must use OpenMobileWallet(AppEntry) or set SelectedWallet on iOS!");
			}

			var url = MobileWalletURLFormatHelper
				.GetURLForMobileWalletOpen(ConnectURL, SelectedWallet.mobile).Split('?')[0];

			Debug.Log("Opening: " + url);
			Application.OpenURL(url);
		#else
			Debug.Log("Platform does not support deep linking");
			return;
		#endif
		}

		public void OpenDeepLink()
		{
			if (!Session.ReadyForUserPrompt)
			{
				Debug.LogError("WalletConnectUnity.ActiveSession not ready for a user prompt" +
				               "\nWait for ActiveSession.ReadyForUserPrompt to be true");
				return;
			}

		#if UNITY_ANDROID
			Application.OpenURL(ConnectURL);
		#elif UNITY_IOS
			if (SelectedWallet == null)
			{
				throw new NotImplementedException(
					"You must use OpenDeepLink(AppEntry) or set SelectedWallet on iOS!");
			}

			var url = MobileWalletURLFormatHelper
				.GetURLForMobileWalletOpen(ConnectURL, SelectedWallet.mobile);

			Debug.Log("[WalletConnect] Opening URL: " + url);

			Application.OpenURL(url);
		#else
			Debug.Log("Platform does not support deep linking");
			return;
		#endif
		}

		public async UniTask CloseSession(bool waitForNewSession = true)
		{
			if (Session == null)
			{
				return;
			}

			await Session.Disconnect();

			if (waitForNewSession)
			{
				await Session.Connect();
			}
		}

		private void SetupEvents()
		{
			if (Session == null)
			{
				Debug.LogError("Trying to setup events on null session");
				return;
			}

			Session.OnSessionDisconnect += SessionOnSessionDisconnect;
			Session.OnSessionCreated += SessionOnSessionCreated;
			Session.OnSessionResumed += SessionOnSessionResumed;
		}

		private void TeardownEvents()
		{
			if (Session == null)
			{
				return;
			}

			Session.OnSessionDisconnect -= SessionOnSessionDisconnect;
			Session.OnSessionCreated -= SessionOnSessionCreated;
			Session.OnSessionResumed -= SessionOnSessionResumed;
		}

		private void SessionOnSessionResumed(object sender, WalletConnectSession e)
		{
			_resumedSessionConnected?.Invoke(e as WalletConnectUnitySession ?? Session);
		}

		private void SessionOnSessionCreated(object sender, WalletConnectSession e)
		{
			_newSessionConnected?.Invoke(e as WalletConnectUnitySession ?? Session);

			var sessionToSave = Session.GetSavedSession();
			SessionSaveHandler.SaveSession(sessionToSave);
		}

		private async Task<WCSessionData> CompleteConnect()
		{
			SetupDefaultWallet().Forget();
			SetupEvents();

			var allEvents = new WalletConnectEventWithSessionData();

			allEvents.AddListener(sessionData => { _connectedEventSession?.Invoke(sessionData); });
			ConnectionStarted?.Invoke();
			var tries = 0;
			while (tries < _connectSessionRetryCount)
			{
				Debug.Log($"Trying to connect session. Try : {tries}");
				try
				{
					var session = await Session.WaitForSessionToConnectAsync();

					allEvents.Invoke(session);
					return session;
				}
				catch (IOException e)
				{
					tries++;

					if (tries >= _connectSessionRetryCount)
					{
						throw new IOException("Failed to request session connection after " + tries + " times.", e);
					}
				}
			}

			throw new IOException("Failed to request session connection after " + tries + " times.");
		}

		private async void SessionOnSessionDisconnect(object sender, EventArgs e)
		{
			_disconnectedEvent?.Invoke(Session);

			if (_autoSaveAndResume && SessionSaveHandler.IsSessionSaved())
			{
				SessionSaveHandler.ClearSession();
			}

			if (_createNewSessionOnSessionDisconnect)
			{
				await Connect();
			}
		}

		private async UniTask SetupDefaultWallet()
		{
			if (_defaultWallet == Wallets.None)
			{
				return;
			}

			var supportedWallets = await WalletDownloadHelper.FetchWalletList(false);

			var wallet =
				supportedWallets.Values.FirstOrDefault(a =>
					string.Equals(a.name, _defaultWallet.GetWalletName(), StringComparison.InvariantCultureIgnoreCase));

			if (wallet != null)
			{
				SelectedWallet = wallet;
				await wallet.DownloadImages();
			}
		}

		private Task SaveOrDisconnect()
		{
			if (Session == null)
			{
				return Task.CompletedTask;
			}

			if (!Session.Connected)
			{
				return Task.CompletedTask;
			}

			if (!_autoSaveAndResume)
			{
				return Session.Disconnect();
			}

			var sessionToSave = Session.GetSavedSession();
			SessionSaveHandler.SaveSession(sessionToSave);

			return Session.Transport.Close();
		}
	}
}