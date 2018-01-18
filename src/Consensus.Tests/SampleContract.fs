﻿module Consensus.Tests.SampleContract

open Consensus
open Types
open Hash
open System.Text
open TxSkeleton
open Crypto

let sampleContractCode = """
open Zen.Types
open Zen.Vector
open Zen.Util

val test: transactionSkeleton -> hash -> transactionSkeleton
let test (Tx pInputs outputs data) hash =
  let output = {
    lock = ContractLock hash 0 Empty;
    spend = {
      asset = hash;
      amount = 1000UL
    }
  } in

  let pInput = {
      txHash = hashFromBase64 "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
      index = 0ul
  }, output in

  let outputs' = VCons output outputs in
  let pInputs' = VCons pInput pInputs in
  Tx pInputs' outputs' data"""

let sampleContractHash = 
    sampleContractCode
    |> Encoding.UTF8.GetBytes
    |> Hash.compute

let private sampleContractTester txSkeleton hash =
    let output = {
        lock = Lock.Contract hash
        spend =
        {
            asset = hash
            amount = 1000UL
        }
    } 

    let pInput =
        {
            txHash = Hash.zero
            index = 0ul
        }, output

    let outputs' = txSkeleton.outputs @ [ output ] 
    let pInputs' = txSkeleton.pInputs @ [ pInput ] 
    { txSkeleton with outputs = outputs'; pInputs = pInputs' }

let sampleKeyPair = KeyPair.create()
let samplePrivateKey, samplePublicKey = sampleKeyPair

let sampleInput = {
    txHash = Hash.zero
    index = 1u
}

let sampleOutput = {
    lock = PK (PublicKey.hash samplePublicKey)
    spend = { asset = Hash.zero; amount = 1UL }
}

let sampleInputTx = 
    {
        pInputs = [ sampleInput, sampleOutput ]
        outputs = [ sampleOutput ]
    }

let sampleOutputTx = 
    sampleContractTester sampleInputTx sampleContractHash

let sampleExpectedResult = 
    sampleOutputTx
    |> Transaction.fromTxSkeleton sampleContractHash
    |> Transaction.addContractWitness sampleContractHash sampleInputTx
    |> Transaction.sign [ sampleKeyPair ]

let getSampleUtxoset utxos =
    Map.add sampleInput (UtxoSet.Unspent sampleOutput) utxos