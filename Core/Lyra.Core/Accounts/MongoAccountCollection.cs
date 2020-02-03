using System;
using System.Collections.Generic;
using Lyra.Core.Blocks;
using Lyra.Core.Blocks.Fees;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson.Serialization.Options;
using System.Linq;
using Lyra.Core.Utils;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Lyra.Core.Accounts
{
    // this is account collection (collection of block chains) used on the node side only
    // 
    public class MongoAccountCollection : IAccountCollectionAsync
    {
        //private const string COLLECTION_DATABASE_NAME = "account_collection";
        private LyraConfig _config;

        private MongoClient _Client;

        private IMongoCollection<TransactionBlock> _blocks;

        readonly string _BlocksCollectionName = null;

        IMongoDatabase _db;

        readonly string _DatabaseName;

        ILogger _log;

        public string Cluster { get; set; }

        public MongoAccountCollection()
        {
            _log = new SimpleLogger("Mongo").Logger;

            _config = Neo.Settings.Default.LyraNode;

            _DatabaseName = _config.Lyra.Database.DatabaseName;

            _BlocksCollectionName = _config.Lyra.NetworkId + "-" + "Primary" + "-blocks";

            BsonClassMap.RegisterClassMap<TransactionBlock>(cm =>
            {
                cm.AutoMap();
                cm.MapMember(c => c.Balances).SetSerializer(new DictionaryInterfaceImplementerSerializer<Dictionary<string, decimal>>(DictionaryRepresentation.ArrayOfDocuments));
            });

            BsonClassMap.RegisterClassMap<SendTransferBlock>();
            BsonClassMap.RegisterClassMap<ExchangingBlock>();
            BsonClassMap.RegisterClassMap<ReceiveTransferBlock>();
            BsonClassMap.RegisterClassMap<OpenWithReceiveTransferBlock>();
            BsonClassMap.RegisterClassMap<LyraTokenGenesisBlock>();
            BsonClassMap.RegisterClassMap<TokenGenesisBlock>();
            BsonClassMap.RegisterClassMap<TradeBlock>();
            BsonClassMap.RegisterClassMap<TradeOrderBlock>();
            BsonClassMap.RegisterClassMap<ExecuteTradeOrderBlock>();
            BsonClassMap.RegisterClassMap<CancelTradeOrderBlock>();
            BsonClassMap.RegisterClassMap<OpenWithReceiveFeeBlock>();
            BsonClassMap.RegisterClassMap<ReceiveFeeBlock>();
            BsonClassMap.RegisterClassMap<ConsolidationBlock>();
            BsonClassMap.RegisterClassMap<ServiceBlock>();
            BsonClassMap.RegisterClassMap<AuthorizationSignature>();
            BsonClassMap.RegisterClassMap<NullTransactionBlock>();

            _blocks = GetDatabase().GetCollection<TransactionBlock>(_BlocksCollectionName);

            Cluster = GetDatabase().Client.Cluster.ToString();

            async Task CreateIndexes(string columnName, bool uniq)
            {
                var options = new CreateIndexOptions() { Unique = uniq };
                var field = new StringFieldDefinition<TransactionBlock>(columnName);
                var indexDefinition = new IndexKeysDefinitionBuilder<TransactionBlock>().Ascending(field);
                var indexModel = new CreateIndexModel<TransactionBlock>(indexDefinition, options);
                await _blocks.Indexes.CreateOneAsync(indexModel);
            }

            async Task CreateNoneStringIndex(string colName, bool uniq)
            {
                var options = new CreateIndexOptions() { Unique = uniq };
                IndexKeysDefinition<TransactionBlock> keyCode = "{ " + colName + ": 1 }";
                var codeIndexModel = new CreateIndexModel<TransactionBlock>(keyCode, options);
                await _blocks.Indexes.CreateOneAsync(codeIndexModel);
            }

            CreateIndexes("_t", false).Wait();
            CreateIndexes("Hash", true).Wait();
            CreateIndexes("PreviousHash", false).Wait();
            CreateIndexes("AccountID", false).Wait();
            CreateNoneStringIndex("UIndex", true).Wait();
            CreateNoneStringIndex("Index", false).Wait();
            CreateNoneStringIndex("BlockType", false).Wait();

            CreateIndexes("SourceHash", false).Wait();
            CreateIndexes("DestinationAccountId", false).Wait();
        }

        /// <summary>
        /// Deletes all blocks and the block collection
        /// </summary>
        public void Delete()
        {
            if (GetClient() == null)
                return;

            if (GetDatabase() == null)
                return;

            GetDatabase().DropCollection(_BlocksCollectionName);
        }

        private MongoClient GetClient()
        {
            if (_Client == null)
                _Client = new MongoClient(_config.Lyra.Database.DBConnect);
            return _Client;
        }

        private IMongoDatabase GetDatabase()
        {
            if (_db == null)
                _db = GetClient().GetDatabase(_DatabaseName);
            return _db;
        }

        public async Task<long> GetBlockCountAsync()
        {
            return await _blocks.CountDocumentsAsync(new BsonDocument());
        }

        public async Task<long> GetBlockCountAsync(string AccountId)
        {
            var filter = new FilterDefinitionBuilder<TransactionBlock>().Eq<string>(a => a.AccountID, AccountId);
            var result = await _blocks.CountDocumentsAsync(filter);

            return result;
        }

        public async Task<bool> AccountExistsAsync(string AccountId)
        {
            var options = new FindOptions<TransactionBlock, TransactionBlock>
            {
                Limit = 1
            };

            var filter = new FilterDefinitionBuilder<TransactionBlock>().Eq<string>(a => a.AccountID, AccountId);
            var result = await _blocks.FindAsync(filter, options);
            return await result.AnyAsync();
        }

        public async Task<ServiceBlock> GetLastServiceBlockAsync()
        {
            var finds = await _blocks.FindAsync(a => a.BlockType == BlockTypes.Service);
            var list = await finds.ToListAsync();
            return list.Last() as ServiceBlock;
        }

        public async Task<ConsolidationBlock> GetSyncBlockAsync()
        {
            var finds = await _blocks.FindAsync(a => a.BlockType == BlockTypes.Consolidation);
            var list = await finds.ToListAsync();
            return list.Last() as ConsolidationBlock;
        }

        private async Task<List<TransactionBlock>> GetAccountBlockListAsync(string AccountId)
        {
            var finds = await _blocks.FindAsync(x => x.AccountID == AccountId);
            var list = await finds.ToListAsync();
            var result = list.OrderBy(y => y.Index).ToList();
            return result;
        }

        public async Task<TransactionBlock> FindLatestBlockAsync()
        {
            var options = new FindOptions<TransactionBlock, TransactionBlock>
            {
                Limit = 1,
                Sort = Builders<TransactionBlock>.Sort.Descending(o => o.UIndex)
            };

            var result = await (await _blocks.FindAsync(FilterDefinition<TransactionBlock>.Empty, options)).FirstOrDefaultAsync();
            return result;
        }

        public async Task<TransactionBlock> FindLatestBlockAsync(string AccountId)
        {
            var options = new FindOptions<TransactionBlock, TransactionBlock>
            {
                Limit = 1,
                Sort = Builders<TransactionBlock>.Sort.Descending(o => o.UIndex)
            };
            var filter = new FilterDefinitionBuilder<TransactionBlock>().Eq<string>(a => a.AccountID, AccountId);

            var result = await (await _blocks.FindAsync(filter, options)).FirstOrDefaultAsync();
            return result;
        }

        public async Task<TokenGenesisBlock> FindTokenGenesisBlockAsync(string Hash, string Ticker)
        {
            //TokenGenesisBlock result = null;
            if (!string.IsNullOrEmpty(Hash))
            {
                var result = await (await _blocks.FindAsync(x => x.Hash == Hash)).FirstOrDefaultAsync();
                if (result != null)
                    return result as TokenGenesisBlock;
            }

            var list = await FindTokenGenesisBlocksAsync(Ticker);
            return list.FirstOrDefault();

            //// to do - try to replace this by indexed search using BlockType indexed field (since we can't index Ticker field):
            //// find all GenesysBlocks first, then check if one of them has the right ticker
            //if (!string.IsNullOrEmpty(Ticker))
            //{
            //    var builder = Builders<TransactionBlock>.Filter;
            //    var filterDefinition = builder.Eq("Ticker", Ticker) & builder.Eq("_t", )

            //    var result = await (await _blocks.FindAsync(filterDefinition)).FirstOrDefaultAsync();
            //    if (result != null)
            //        return result as TokenGenesisBlock;
            //}

            //return null;
        }

        public async Task<List<TokenGenesisBlock>> FindTokenGenesisBlocksAsync(string keyword)
        {
            var builder = Builders<TransactionBlock>.Filter;
            var filterDefinition = builder.Eq("_t", "TokenGenesisBlock");
            var result = await _blocks.FindAsync(filterDefinition);

            if (string.IsNullOrEmpty(keyword))
            {
                return result.ToList().Cast<TokenGenesisBlock>().ToList();
            }
            else
            {
                return result.ToList().Cast<TokenGenesisBlock>().Where(a => a.Ticker.Contains(keyword)).ToList();
            }
        }

        public async Task<NullTransactionBlock> FindNullTransBlockByHashAsync(string hash)
        {
            var builder = Builders<TransactionBlock>.Filter;
            var filterDefinition = builder.Eq("_t", "NullTransactionBlock") & builder.Eq("FailedBlockHash", hash);
            var result = await _blocks.FindAsync(filterDefinition);

            return await result.FirstOrDefaultAsync() as NullTransactionBlock;
        }

        public async Task<TransactionBlock> FindBlockByHashAsync(string hash)
        {
            var filter = new FilterDefinitionBuilder<TransactionBlock>().Eq<string>(a => a.Hash, hash);

            var block = await (await _blocks.FindAsync(filter)).FirstOrDefaultAsync();
            return block;
        }

        public async Task<TransactionBlock> FindBlockByHashAsync(string AccountId, string hash)
        {
            var builder = new FilterDefinitionBuilder<TransactionBlock>();
            var filter = builder.Eq(a => a.Hash, hash) & builder.Eq(a => a.AccountID, AccountId);

            var block = await (await _blocks.FindAsync(filter)).FirstOrDefaultAsync();
            return block;
        }

        public async Task<List<NonFungibleToken>> GetNonFungibleTokensAsync(string AccountId)
        {

            var p1 = new BsonArray();
            p1.Add(BlockTypes.ReceiveTransfer.ToString());
            p1.Add(BlockTypes.OpenAccountWithReceiveTransfer.ToString());
            p1.Add(BlockTypes.OpenAccountWithImport.ToString());

            var builder = Builders<TransactionBlock>.Filter;
            var filterDefinition = builder.And(builder.In("BlockType", p1), builder.And(builder.Eq("AccountID", AccountId), builder.Ne("NonFungibleToken", BsonNull.Value)));

            var allNonFungibleReceiveBlocks = await (await _blocks.FindAsync(filterDefinition)).ToListAsync();

            var the_list = new List<NonFungibleToken>();

            foreach (TransactionBlock receiveBlock in allNonFungibleReceiveBlocks)
            {
                the_list.Add(receiveBlock.NonFungibleToken);
            }

            if (the_list.Count > 0)
                return the_list;

            return null;
        }


        public async Task<TransactionBlock> FindBlockByPreviousBlockHashAsync(string previousBlockHash)
        {
            var result = await (await _blocks.FindAsync(x => x.PreviousHash.Equals(previousBlockHash))).FirstOrDefaultAsync();
            return result;
        }

        /// <summary>
        /// Ignores fee blocks!
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public async Task<ReceiveTransferBlock> FindBlockBySourceHashAsync(string hash)
        {
            var builder = Builders<TransactionBlock>.Filter;
            var filterDefinition = builder.Eq("SourceHash", hash);

            var result = await (await _blocks.FindAsync(filterDefinition)).ToListAsync();

            foreach (var block in result)
            {
                if (block.BlockType == BlockTypes.OpenAccountWithReceiveFee || block.BlockType == BlockTypes.ReceiveFee)
                    continue;
                else
                    return block as ReceiveTransferBlock;
            }
            return null;
        }


        public async Task<TransactionBlock> FindBlockByIndexAsync(string AccountId, Int64 index)
        {
            var builder = new FilterDefinitionBuilder<TransactionBlock>();
            var filter = builder.Eq(a => a.AccountID, AccountId) & builder.Eq(a => a.Index, index);

            var block = await (await _blocks.FindAsync(filter)).FirstOrDefaultAsync();
            return block;
        }

        private async Task<ReceiveTransferBlock> FindLastRecvBlock(string AccountId)
        {
            var options = new FindOptions<TransactionBlock, TransactionBlock>
            {
                Limit = 1,
                Sort = Builders<TransactionBlock>.Sort.Descending(o => o.UIndex)
            };
            var builder = new FilterDefinitionBuilder<TransactionBlock>();
            var filter = builder.Eq(a => a.AccountID, AccountId) & builder.Eq(a => a.BlockType, BlockTypes.ReceiveTransfer);

            var result = await (await _blocks.FindAsync(filter, options)).FirstOrDefaultAsync();
            return result as ReceiveTransferBlock;
        }

        public async Task<SendTransferBlock> FindUnsettledSendBlockAsync(string AccountId)
        {
            // get last settled receive block
            var lastRecvBlock = await FindLastRecvBlock(AccountId);

            long fromUIndex = 0;
            if (lastRecvBlock != null)
                fromUIndex = lastRecvBlock.UIndex;

            // First, let find all send blocks:
            // (It can be optimzed as it's going to be growing, so it can be called with munimum Service Chain Height parameter to look only for recent blocks) 
            var builder = Builders<TransactionBlock>.Filter;
            var filterDefinition = builder.Eq("DestinationAccountId", AccountId) & builder.Gt("UIndex", fromUIndex);

            var allSendBlocks = await (await _blocks.FindAsync(filterDefinition)).ToListAsync();

            foreach (SendTransferBlock sendBlock in allSendBlocks)
            {
                //// Now, let's try to fetch the corresponding receive block:
                var p1 = new BsonArray();
                p1.Add((int)BlockTypes.ReceiveTransfer);
                p1.Add((int)BlockTypes.OpenAccountWithReceiveTransfer);
                p1.Add((int)BlockTypes.OpenAccountWithImport);
                p1.Add((int)BlockTypes.ImportAccount);

                var builder1 = Builders<TransactionBlock>.Filter;
                var filterDefinition1 = builder1.And(builder1.In("BlockType", p1), builder1.And(builder1.Eq("AccountID", AccountId), builder1.Eq("SourceHash", sendBlock.Hash)));

                var result = await (await _blocks.FindAsync(filterDefinition1)).FirstOrDefaultAsync();

                if (result == null)
                    return sendBlock;

                //var any_receive_block_with_this_source = FindBlockBySourceHash(sendBlock.Hash);
                //if (any_receive_block_with_this_source == null)

            }
            return null;
        }

        /// <summary>
        /// Returns the first unexecuted and incancelled trade aimed to an order created on the account.
        /// </summary>
        /// <param name="AccountId"></param>
        /// <param name="BuyTokenCode">
        /// The code of the token being purchased (optional).
        /// </param>
        /// <param name="SellTokenCode">
        /// The code of the token being sold (optional).
        /// </param>
        /// <returns></returns>
        public TradeBlock FindUnexecutedTrade(string AccountId, string BuyTokenCode, string SellTokenCode)
        {
            if (BuyTokenCode == "*")
                BuyTokenCode = null;

            if (SellTokenCode == "*")
                SellTokenCode = null;

            // First, let find all the trade blocks aimed to this account:
            //var trades = _blocks.Find(Query.And(Query.EQ("BlockType", BlockTypes.Trade.ToString()), Query.EQ("DestinationAccountId", AccountId)));

            var trades_builder = Builders<TransactionBlock>.Filter;
            var trades_filterDefinition = trades_builder.And(trades_builder.Eq("BlockType", BlockTypes.Trade.ToString()), trades_builder.Eq("DestinationAccountId", AccountId));

            var trades = _blocks.Find(trades_filterDefinition).ToList();

            foreach (TradeBlock trade in trades)
            {
                var exec_builder = Builders<TransactionBlock>.Filter;
                var exec_filterDefinition = exec_builder.And(exec_builder.Eq("BlockType", BlockTypes.ExecuteTradeOrder.ToString()), exec_builder.Eq("TradeId", trade.Hash));
                var trade_execution = _blocks.Find(exec_filterDefinition);

                if (trade_execution.Any())
                    continue;

                var cancel_builder = Builders<TransactionBlock>.Filter;
                var cancel_filterDefinition = cancel_builder.And(cancel_builder.Eq("BlockType", BlockTypes.CancelTradeOrder.ToString()), cancel_builder.Eq("TradeOrderId", trade.TradeOrderId));
                var trade_cancellation = _blocks.Find(cancel_filterDefinition);

                if (trade_cancellation.Any())
                    continue;

                if (!string.IsNullOrEmpty(BuyTokenCode) && BuyTokenCode != trade.BuyTokenCode)
                    continue;

                if (!string.IsNullOrEmpty(SellTokenCode) && SellTokenCode != trade.SellTokenCode)
                    continue;

                return trade;
            }
            return null;
        }

        public List<TradeOrderBlock> GetTradeOrderBlocks()
        {
            var list = new List<TradeOrderBlock>();

            //var blocks = _blocks.Find(Query.EQ("BlockType", BlockTypes.TradeOrder.ToString()));

            var builder = Builders<TransactionBlock>.Filter;
            var filterDefinition = builder.Eq("BlockType", BlockTypes.TradeOrder.ToString());
            var trade_blocks = _blocks.Find(filterDefinition).ToList();

            foreach (TradeOrderBlock block in trade_blocks)
                list.Add(block);

            return list;
        }

        // returns the list of hashes (order IDs) of all cancelled trade order blocks
        public List<string> GetTradeOrderCancellations()
        {
            var list = new List<string>();
            //var blocks = _blocks.Find(Query.EQ("BlockType", BlockTypes.CancelTradeOrder.ToString()));

            var builder = Builders<TransactionBlock>.Filter;
            var filterDefinition = builder.Eq("BlockType", BlockTypes.CancelTradeOrder.ToString());
            var blocks = _blocks.Find(filterDefinition).ToList();

            foreach (CancelTradeOrderBlock block in blocks)
                list.Add(block.TradeOrderId);

            return list;
        }

        // returns the list of hashes (order IDs) of all cancelled trade order blocks
        public List<string> GetExecutedTradeOrderBlocks()
        {
            var list = new List<string>();
            //var blocks = _blocks.Find(Query.EQ("BlockType", BlockTypes.ExecuteTradeOrder.ToString()));
            var builder = Builders<TransactionBlock>.Filter;
            var filterDefinition = builder.Eq("BlockType", BlockTypes.ExecuteTradeOrder.ToString());
            var blocks = _blocks.Find(filterDefinition).ToList();

            foreach (ExecuteTradeOrderBlock block in blocks)
                list.Add(block.TradeOrderId);

            return list;
        }

        public async Task<bool> AddBlockAsync(TransactionBlock block)
        {
            if (block.Index == 0 || block.UIndex == 0)
            {
                _log.LogWarning("AccountCollection=>AddBlock: Block with zero index/UIndex is now allowed!");
                return false;
            }

            //if (null != await GetBlockByUIndexAsync(block.UIndex))
            //{
            //    _log.LogWarning("AccountCollection=>AddBlock: Block with such UIndex already exists!");
            //    return false;
            //}

            //if (await FindBlockByHashAsync(block.Hash) != null)
            //{
            //    _log.LogWarning("AccountCollection=>AddBlock: Block with such Hash already exists!");
            //    return false;
            //}

            //if (block.BlockType != BlockTypes.NullTransaction && await FindBlockByIndexAsync(block.AccountID, block.Index) != null)
            //{
            //    _log.LogWarning("AccountCollection=>AddBlock: Block with such Index already exists!");
            //    return false;
            //}

            _log.LogInformation($"AddBlockAsync InsertOneAsync: {block.UIndex}/{block.Index}");
            await _blocks.InsertOneAsync(block);
            return true;
        }

        public void Dispose()
        {
            // nothing to dispose
        }

        public async Task<long> GetNewestBlockUIndexAsync()
        {
            var result = await FindLatestBlockAsync();
            if (result == null)
                return 0;
            return result.UIndex;
        }

        public async Task<TransactionBlock> GetBlockByUIndexAsync(long uindex)
        {
            var filter = new FilterDefinitionBuilder<TransactionBlock>().Eq(a => a.UIndex, uindex);

            var block = await (await _blocks.FindAsync(filter)).FirstOrDefaultAsync();
            return block;
        }

    }
}