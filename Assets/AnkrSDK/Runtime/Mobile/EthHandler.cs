using System;
using System.Numerics;
using System.Threading.Tasks;
using AnkrSDK.Core.Infrastructure;
using AnkrSDK.Utils;
using AnkrSDK.WalletConnectSharp.Core.Models.Ethereum;
using AnkrSDK.WalletConnectSharp.Unity;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.Eth.Transactions;
using Nethereum.Web3;
using UnityEngine;

namespace AnkrSDK.Mobile
{
	public class EthHandler : IEthHandler
	{
		private readonly IWeb3 _web3Provider;
		private readonly WalletConnect _walletConnect;
		private readonly ISilentSigningHandler _silentSigningHandler;

		public EthHandler(IWeb3 web3Provider, ISilentSigningHandler silentSigningHandler)
		{
			_web3Provider = web3Provider;
			_silentSigningHandler = silentSigningHandler;
			_walletConnect = ConnectProvider<WalletConnect>.GetWalletConnect();
		}

		public Task<string> GetDefaultAccount()
		{
			if (_walletConnect.Session == null)
			{
				throw new Exception("Application is not linked to wallet");
			}

			var activeSessionAccount = _walletConnect.Session.Accounts[0];
			if (string.IsNullOrEmpty(activeSessionAccount))
			{
				Debug.LogError("Account is null");
			}

			return Task.FromResult(activeSessionAccount);
		}

		public Task<BigInteger> GetChainId()
		{
			if (_walletConnect.Session == null)
			{
				throw new Exception("Application is not linked to wallet");
			}

			var chainId = _walletConnect.Session.ChainId;
			return Task.FromResult(new BigInteger(chainId));
		}

		public Task<TransactionReceipt> GetTransactionReceipt(string transactionHash)
		{
			return _web3Provider.TransactionManager.TransactionReceiptService.PollForReceiptAsync(transactionHash);
		}

		public Task<Transaction> GetTransaction(string transactionReceipt)
		{
			var transactionByHash = new EthGetTransactionByHash(_web3Provider.Client);
			return transactionByHash.SendRequestAsync(transactionReceipt);
		}

		public Task<HexBigInteger> EstimateGas(
			string from,
			string to,
			string data = null,
			string value = null,
			string gas = null,
			string gasPrice = null,
			string nonce = null
		)
		{
			var transactionInput = new TransactionInput(to, from)
			{
				Gas = gas != null ? new HexBigInteger(gas) : null,
				GasPrice = gasPrice != null ? new HexBigInteger(gasPrice) : null,
				Nonce = nonce != null ? new HexBigInteger(nonce) : null,
				Value = value != null ? new HexBigInteger(value) : null,
				Data = data
			};

			return EstimateGas(transactionInput);
		}

		public Task<string> Sign(string messageToSign, string address)
		{
			if (_silentSigningHandler != null && _silentSigningHandler.IsSilentSigningActive())
			{
				_silentSigningHandler.SilentSignMessage(messageToSign, address);
			}

			return _walletConnect.Session.EthSign(address, messageToSign);
		}

		public async Task<string> SendTransaction(string from, string to, string data = null, string value = null,
			string gas = null,
			string gasPrice = null, string nonce = null)
		{
			if (_silentSigningHandler != null && _silentSigningHandler.IsSilentSigningActive())
			{
				var hash = await _silentSigningHandler.SendSilentTransaction(from, to, data, value, gas, gasPrice,
					nonce);
				return hash;
			}

			var transactionData = new TransactionData
			{
				from = from,
				to = to,
				data = data,
				value = value != null ? AnkrSDKHelper.StringToBigInteger(value) : null,
				gas = gas != null ? AnkrSDKHelper.StringToBigInteger(gas) : null,
				gasPrice = gasPrice != null ? AnkrSDKHelper.StringToBigInteger(gasPrice) : null,
				nonce = nonce
			};
			var request = new AnkrSDK.WalletConnectSharp.Core.Models.Ethereum.EthSendTransaction(transactionData);
			var response = await _walletConnect.Session
				.Send<AnkrSDK.WalletConnectSharp.Core.Models.Ethereum.EthSendTransaction, EthResponse>(request);
			return response.Result;
		}

		public Task<HexBigInteger> EstimateGas(TransactionInput transactionInput)
		{
			return _web3Provider.TransactionManager.EstimateGasAsync(transactionInput);
		}

		public async Task<BigInteger> GetBalance(string address)
		{
			if (address == null)
			{
				address = await GetDefaultAccount();
			}

			var balance = await _web3Provider.Eth.GetBalance.SendRequestAsync(address);
			return balance.Value;
		}

		public async Task<BigInteger> GetBlockNumber()
		{
			var blockNumber = await _web3Provider.Eth.Blocks.GetBlockNumber.SendRequestAsync();
			return blockNumber.Value;
		}

		public async Task<BigInteger> GetTransactionCount(string hash)
		{
			var blockNumber = await _web3Provider.Eth.Blocks.GetBlockTransactionCountByHash.SendRequestAsync(hash);
			return blockNumber.Value;
		}

		public async Task<BigInteger> GetTransactionCount(BlockParameter block)
		{
			var blockNumber = await _web3Provider.Eth.Blocks.GetBlockTransactionCountByNumber.SendRequestAsync(block);
			return blockNumber.Value;
		}

		public Task<BlockWithTransactions> GetBlockWithTransactions(string hash)
		{
			return _web3Provider.Eth.Blocks.GetBlockWithTransactionsByHash.SendRequestAsync(hash);
		}

		public Task<BlockWithTransactions> GetBlockWithTransactions(BlockParameter block)
		{
			return _web3Provider.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(block);
		}

		public Task<BlockWithTransactionHashes> GetBlockWithTransactionsHashes(string hash)
		{
			return _web3Provider.Eth.Blocks.GetBlockWithTransactionsHashesByHash.SendRequestAsync(hash);
		}

		public Task<BlockWithTransactionHashes> GetBlockWithTransactionsHashes(BlockParameter block)
		{
			return _web3Provider.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(block);
		}
	}
}