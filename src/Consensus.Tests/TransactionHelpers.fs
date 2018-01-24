﻿module Consensus.Tests.TransactionHelpers

open Consensus
open Consensus.Types
open TransactionValidation

let getSignedTx tx keys =
    let signedTx = Transaction.sign keys tx
    let txHash = Transaction.hash signedTx
    signedTx, txHash

let private inputsValidation acs utxos signedTx txHash =
    let tryGetUTXO _ = None
    let getWallet _ = Map.empty
    
    validateInputs tryGetUTXO getWallet acs utxos ContractWallets.asDatabase txHash signedTx
    |> Result.map fst

let inputsValidationMsg msg acs utxos tx keys =
    let signedTx, txHash = getSignedTx tx keys
    inputsValidation acs utxos signedTx txHash, 
    (Error (General msg) : Result<Transaction, ValidationError>)

let inputsValidationOk acs utxos tx keys =
    let signedTx, txHash = getSignedTx tx keys
    inputsValidation acs utxos signedTx txHash, 
    (Ok signedTx : Result<Transaction, ValidationError>)

let basicValidationMsg msg tx =
    validateBasic tx, 
    (Error (General msg): Result<Transaction, ValidationError>)

let basicValidationOk tx =
    validateBasic tx, 
    (Ok tx : Result<Transaction, ValidationError>)

let inputsValidationOrphan acs utxos tx keys =
    let signedTx, txHash = getSignedTx tx keys
    inputsValidation acs utxos signedTx txHash, 
    (Error Orphan : Result<Transaction, ValidationError>)
