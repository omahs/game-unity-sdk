using MirageSDK.UseCases;
using UnityEngine;

namespace MirageSDK.Ads
{
	public class AdsUseCaseController : UseCase
	{
		[SerializeField] private AdsCallbackListener _adsCallbackListener;

		private void OnEnable()
		{
			_adsCallbackListener.ActivateBillboardAds(false);

			_adsCallbackListener.GetInitializeButton().onClick.AddListener(OnInitializeButtonClick);
			_adsCallbackListener.GetLoadBannerAdButton().onClick.AddListener(OnLoadImageButtonClick);
			_adsCallbackListener.GetLoadFullscreenAdButton().onClick.AddListener(OnLoadFullscreenAdButtonClick);
			_adsCallbackListener.GetViewButton().onClick.AddListener(OnViewButtonClick);
		}

		private void OnDisable()
		{
			_adsCallbackListener.GetInitializeButton().onClick.RemoveAllListeners();
			_adsCallbackListener.GetLoadBannerAdButton().onClick.RemoveAllListeners();
			_adsCallbackListener.GetLoadFullscreenAdButton().onClick.RemoveAllListeners();
			_adsCallbackListener.GetViewButton().onClick.RemoveAllListeners();
			_adsCallbackListener.UnsubscribeToCallbackListenerEvents();
		}

		public override void DeActivateUseCase()
		{
			base.DeActivateUseCase();
			_adsCallbackListener.ActivateBillboardAds(false);
		}

		private void OnInitializeButtonClick()
		{
			const string walletAddress = "This is Mirage mobile address";
			_adsCallbackListener.UnsubscribeToCallbackListenerEvents();
			_adsCallbackListener.SubscribeToCallbackListenerEvents();

			MirageAdvertisements.Initialize(AdsBackendInformation.TestAppId, walletAddress);
		}

		private void OnLoadFullscreenAdButtonClick()
		{
			MirageAdvertisements.LoadAd(AdsBackendInformation.FullscreenAdTestUnitId);
		}

		private void OnLoadImageButtonClick()
		{
			MirageAdvertisements.LoadAdTexture(AdsBackendInformation.BannerAdTestUnitId);
		}

		private void OnViewButtonClick()
		{
			MirageAdvertisements.ShowAd(AdsBackendInformation.FullscreenAdTestUnitId);
		}
	}
}