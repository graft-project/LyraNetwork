﻿
using Lyra.Core.API;

namespace Lyra.Core.Blocks
{
    //public class TransferInfo: TransactionInfo
    //{
    //    public int Precision { get; set; }
    //    public string ServiceHash { get; set; }
    //}


    public class SendTransferBlock : TransactionBlock//, IFeebleBlock
    {

        // the address (account id, public key) of the recipient
        public string DestinationAccountId { get; set; }

        // Optional ID that can be used by the recipient to request the status of transaction without compromising identity
        // Can be assigned either by sender or recipient
        // If generated by the recipient, it can be included in the QR code and read by the sender "offline", 
        // so the recipient's thin wallet can locate the send block
        // by listening to the broadcasts or by making a request to a full node, 
        // without the need to scan all chains with viewkey  
        // TO DO: create new separate block for send transfer; make this block abstract base class for all sending blocks
        //public string PaymentID { get; set; }

        protected override string GetExtraData()
        {
            string extraData = base.GetExtraData();
            extraData = extraData + DestinationAccountId + "|";
            return extraData;
        }

        public override BlockTypes GetBlockType()
        {
            return BlockTypes.SendTransfer;
        }

        public override TransactionInfoEx GetTransaction(TransactionBlock previousBlock)
        {
            // previous block cannot be null for send block as you have to have something to send
            if (previousBlock == null)
                return null;

            var transaction = new TransactionInfoEx() { TokenCode = LyraGlobal.LYRA_TICKER_CODE, Amount = 0 };

            // let's find te balance that was changed since the previous block - to determine the token being transacted
            foreach (var balance in this.Balances)
                if (previousBlock.Balances[balance.Key] != balance.Value && balance.Key != LyraGlobal.LYRA_TICKER_CODE)
                {
                    transaction.TokenCode = balance.Key;
                    transaction.Amount = previousBlock.Balances[balance.Key] - this.Balances[balance.Key];
                    transaction.TotalBalanceChange = transaction.Amount;
                    break;
                }

            // if no token is being transfered, it's default token (like LYR ot LGT depending on configuration) itself
            if (transaction.TokenCode == LyraGlobal.LYRA_TICKER_CODE)
            {
                transaction.Amount = previousBlock.Balances[LyraGlobal.LYRA_TICKER_CODE] - this.Balances[LyraGlobal.LYRA_TICKER_CODE] - this.Fee;
                transaction.TotalBalanceChange = transaction.Amount + this.Fee;
            }

            transaction.FeeCode = this.FeeCode;
            transaction.FeeAmount = this.Fee;

            return transaction;
        }

        //// Returns the non-fungible token being transacted in THIS block.
        //// The NonFungibleTokens list might contain multiple non-fungible tokens transacted in previous blocks
        //public override INonFungibleToken GetNonFungibleTransaction(TransactionBlock previousBlock)
        //{
        //    // previous block cannot be null as you shold have something to receive the non-fungible token
        //    if (previousBlock == null)
        //        return null;

        //    // if there are no nonfungible tokens there is nothing to send!
        //    if (this.NonFungibleTokens == null || this.NonFungibleTokens.Count == 0)
        //        return null;

        //    // let's simply find the first token in previous block that is not present in this block
        //    foreach (var token in this.NonFungibleTokens)
        //    {
        //        if (previousBlock.NonFungibleTokens == null || previousBlock.NonFungibleTokens.Count == 0)
        //            return token;

        //        bool found = false;
        //        foreach (var previous_token in previousBlock.NonFungibleTokens)
        //        {
        //            if (token.TokenCode == previous_token.TokenCode &&
        //                token.Denomination == previous_token.Denomination &&
        //                token.SerialNumber == previous_token.SerialNumber)
        //            {
        //                found = true;
        //                break;
        //            }
        //        }
        //        if (!found)
        //            return token;
        //    }
        //    return null;
        //}

        public override string Print()
        {
            string result = base.Print();
            result += $"DestinationAccountId: {DestinationAccountId}\n";
            return result;
        }



        //public override bool ValidateTransaction(TransactionBlock previousBlock)
        //{
        //    if (!base.ValidateTransaction(previousBlock))
        //        return false;

        //    var trs = CalculateTransaction(previousBlock);

        //    long TransferAmount = Transaction.Amount;
        //    //if (Transaction.TokenCode == FeeCode)
        //        //TransferAmount += Fee;

        //    if (trs.Amount != TransferAmount)
        //        return false;

        //    return true;
        //}





    }
}