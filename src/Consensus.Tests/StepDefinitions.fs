module Consensus.Tests.StepDefinitions

open TechTalk.SpecFlow
open NUnit.Framework
open TechTalk.SpecFlow.BindingSkeletons
open TechTalk.SpecFlow.Assist
open Infrastructure.Platform
open Infrastructure.Result
open System.Text.RegularExpressions
open Blockchain
open Consensus
open ZFStar
open Crypto
open Types
open FStar.UInt
open System
open Wallet
open Blockchain.State
open System.IO
open FsUnit
open Messaging.Events

let private result = new ResultBuilder<string>()

let private (?=) expected actual = actual |> should equal expected
//let private (?=) expected actual = printfn "\nActual: %A\nExpected: %A" actual expected

[<Literal>]
let rlimit = 2723280u

let chain = Chain.getChainParameters Chain.Local
let timestamp = 1515594186383UL + 1UL

let tempDir () =
    Path.Combine
        [| Path.GetTempPath(); Path.GetRandomFileName() |]

let contractPath = tempDir()
let dataPath = tempDir()
let mutable databaseContext = DatabaseContext.createEmpty dataPath
let mutable session = DatabaseContext.createSession databaseContext

let clean() =
    cleanDirectory dataPath
    cleanDirectory contractPath

[<Binding>]
module Binding =
    type TestingState = {
        blocks : Map<string, BlockHeader>
        contracts : Map<string, ContractId * ContractV0>
        keys : Map<string, KeyPair>
        txs : Map<string, Transaction>
        data : Map<string, Zen.Types.Data.data>
    }

    let mutable testingState = {
        blocks = Map.empty
        contracts = Map.empty
        keys = Map.empty
        txs = Map.empty
        data = Map.empty
    }

    let getEmptyState() =
        let ema : EMA.T = {
            difficulty = 0ul
            delayed = []
        }

        let tipState : TipState = {
            tip = ExtendedBlockHeader.empty
            activeContractSet = ActiveContractSet.empty
            ema = ema
        }

        let memoryState : MemoryState = {
            utxoSet = UtxoSet.asDatabase
            activeContractSet = ActiveContractSet.empty
            orphanPool = OrphanPool.create()
            mempool = MemPool.empty
            contractCache = ContractCache.empty
            contractStates = ContractStates.asDatabase
        }

        {
            tipState = tipState
            memoryState = memoryState
            initialBlockDownload = InitialBlockDownload.Inactive
            headers = 0ul
        }

    let mutable state : Blockchain.State.State = getEmptyState()
    let mutable contractExecutionCache : Map<ContractId, Contract.T> = Map.empty


    let getUTXO = UtxoSetRepository.get
    let getContractState = ContractStateRepository.get

    let split (value:string) =
        value.Split [|','|]
        |> Array.choose (fun value -> if String.IsNullOrWhiteSpace value then None else Some value)
        |> Array.toList

    let (|Regex|_|) pattern input =
        let m = Regex.Match(input, pattern)
        if m.Success then Some(List.tail [ for g in m.Groups -> g.Value ])
        else None

    let updateTx txLabel tx =
        testingState <- { testingState with txs = Map.add txLabel tx testingState.txs }
        tx

    let tryFindTx txLabel = Map.tryFind txLabel testingState.txs

    let tryFindContract contractLabel = Map.tryFind contractLabel testingState.contracts

    let findBlock blockLabel = Map.find blockLabel testingState.blocks

    let getAsset value =
        if value = "Zen" then Asset.Zen
        else
            match tryFindContract value with
            | Some (contractId, _) ->
                Asset (contractId, Hash.zero)
            | None ->
                failwithf "Unrecognized asset: %A" value

    let getAmount asset amount =
        if asset = "Zen" then
            amount * 100_000_000UL
        else
            amount

    let findTx txLabel =
        match tryFindTx txLabel with
        | Some tx -> tx
        | None -> failwithf "Referenced tx missing: %A" txLabel

    let tryFindKey keyLabel = Map.tryFind keyLabel testingState.keys

    let findKey keyLabel =
        match tryFindKey keyLabel with
        | Some tx -> tx
        | None -> failwithf "Referenced key missing: %A" keyLabel

    let initTx txLabel =
        match tryFindTx txLabel with
        | Some tx ->
            tx
        | None ->
            { inputs = []; witnesses = []; outputs = []; version = Version0; contract = None }
            |> updateTx txLabel

    let getContractRecord contractLabel =
        match Map.tryFind contractLabel testingState.contracts with
        | Some value -> value
        | None ->
            let path = Path.GetDirectoryName (System.Reflection.Assembly.GetExecutingAssembly().Location)
            let path = Path.Combine (path, "Contracts")
            let contract = Path.ChangeExtension (contractLabel,".fst")
            let code = Path.Combine (path, contract) |> File.ReadAllText

            let hints = match Contract.recordHints code with
                       | Ok hints -> hints
                       | Error error -> failwith error

            let queries = match Infrastructure.ZFStar.totalQueries hints with
                       | Ok queries -> queries
                       | Error error -> failwith error

            let contractId = Contract.makeContractId Version0 code
            let contract = {
               code = code
               hints = hints
               rlimit = rlimit
               queries = queries
            }

            testingState <-
                { testingState with contracts = Map.add contractLabel (contractId, contract) testingState.contracts }

            contractId, contract

//    let initContract contractLabel =
//        match tryFindContract contractLabel with
//        | Some contractId ->
//            ActiveContractSet.tryFind contractId state.memoryState.activeContractSet
//            |> Option.get
//        | None ->
//
//            let contract =
//                getContractCode contractLabel
//                |> compile
//                |> Infrastructure.Result.get
//
//            testingState <-
//                { testingState with contracts = Map.add contractLabel contract.contractId testingState.contracts }
//
//            let contractsCache =
//                state.memoryState.contractCache
//                |> ContractCache.add contract
//
//            let activeContractSet =
//                state.memoryState.activeContractSet
//                |> ActiveContractSet.add contract.contractId contract
//
//            state <- { state with memoryState = { state.memoryState with contractCache = contractsCache; activeContractSet = activeContractSet }
//                                  tipState = { state.tipState with activeContractSet = activeContractSet } }
//
//            contract

        let keyPair =
            match Native.secp256k1_ec_pubkey_create (context, publicKey, secretKey) with
            | Native.Result.Ok -> SecretKey secretKey, PublicKey.PublicKey publicKey
            | _ -> failwithf "Unexpected result generating test key"

        testingState <- { testingState with keys = Map.add keyLabel keyPair testingState.keys }

        keyPair

    let initData dataLabel data =
        if not <| Map.containsKey dataLabel testingState.data then
            testingState <- { testingState with data = Map.add dataLabel data testingState.data }

    let tryFindData dataLabel = Map.tryFind dataLabel testingState.data

    let getData dataType (value : string) =
        match dataType with
        | "string" ->
            value.Trim('"')
            |> fsToFstString
            |> Zen.Types.Data.String
        | "i64" ->
            Int64.Parse value
            |> Zen.Types.Data.I64
        | "u32" ->
            UInt32.Parse value
            |> Zen.Types.Data.U32
        | "u64" ->
            UInt64.Parse value
            |> Zen.Types.Data.U64
        | _ ->
            failwithf "Unrecognized data type: %A" dataType

    let getDataCollection collectionType dataType =
        split
        >> List.map (fun value ->
            match tryFindData value with
            | Some data -> data
            | None -> getData dataType value)
        >> match collectionType with
            | "list" -> fsToFstList >> Zen.Types.Data.List
            | "array" -> Array.ofList >> Zen.Types.Data.Array
            | _ -> failwithf "Unrecognized data collection type: %A" dataType
        >> Zen.Types.Data.Collection

    let getLock label =
        match tryFindContract label with
        | Some (contractId, _) ->
            contractId
            |> Lock.Contract
        | None ->
            initKey label
            |> snd
            |> PublicKey.hash
            |> Lock.PK

    let getOutput amount asset keyLabel = {
        spend = { amount = amount; asset = getAsset asset }
        lock = getLock keyLabel
    }

    let getSender sender =
        if sender = "anonymous" then
            None
        else
//            match tryFindContract sender with
//            | Some contract ->
//                Contract.makeContractId Version0 contract.code
//                |> Consensus.Types.ContractSender
//            | None ->
            initKey sender
            |> snd
            |> Some

    let [<BeforeScenario>] SetupScenario () =
        clean()
        databaseContext <- DatabaseContext.createEmpty dataPath
        session <- DatabaseContext.createSession databaseContext

        testingState <- {
            blocks = Map.empty
            contracts = testingState.contracts
            keys = Map.empty
            txs = Map.empty
            data = Map.empty
        }
        let contractCache = state.memoryState.contractCache
        state <- getEmptyState()
        state <- { state with memoryState = { state.memoryState with contractCache = contractCache } }

    let [<AfterScenario>] TeardownScenario () =
        clean()

    // Initializes a utxoset
    let [<Given>] ``utxoset`` (table: Table) =
        let mutable txs = Map.empty

        // init all mentioned txs with their outputs
        for row in table.Rows do
            let txLabel = row.GetString "Tx"
            let keyLabel = row.GetString "Key"
            let asset = row.GetString "Asset"
            let amount = row.GetInt64 "Amount" |> uint64

            let tx = initTx txLabel
            let output = getOutput (getAmount asset amount) asset keyLabel
            let tx = { tx with outputs = Infrastructure.List.add output tx.outputs }
                     |> updateTx txLabel

            txs <- Map.add txLabel tx txs

        // fold on mentioned txs, fold on their outputs
        let utxoSet =
            txs
            |> Map.fold (fun utxoset _ tx ->
                let txHash = Transaction.hash tx
                tx.outputs
                |> List.mapi (fun i output -> (uint32 i, output))
                |> List.fold (fun utxoset (index, output) ->
                    let outpoint = { txHash = txHash; index = index }
                    Map.add outpoint (UtxoSet.Unspent output) utxoset
                ) utxoset
            ) state.memoryState.utxoSet

        state <- { state with memoryState = { state.memoryState with utxoSet = utxoSet } }

    let executeContract contractLabel inputTxLabel outputTxLabel command sender messageBody =
        let contractId, contract =
            getContractRecord contractLabel
        let contract =
            Contract.compile contractPath contract
            |> Result.bind (fun _ -> Contract.load contractPath 1ul contract.code contractId)
            |> Infrastructure.Result.get

        let tx = initTx inputTxLabel
        let outputs =
            match UtxoSet.tryGetOutputs (getUTXO session) state.memoryState.utxoSet tx.inputs with
            | Some outputs -> outputs
            | None -> failwithf "111unable to get utxos for tx %A" inputTxLabel

        let inputTx = TxSkeleton.fromTransaction tx outputs

        contractExecutionCache <- Map.add contractId contract contractExecutionCache
        let state' = { state with memoryState = { state.memoryState with activeContractSet = ActiveContractSet.add contractId contract state.memoryState.activeContractSet } }

        match TransactionHandler.executeContract session inputTx 0UL contract.contractId command sender messageBody state' false with
        | Ok tx -> updateTx outputTxLabel tx
        | Error error -> failwith error

    // Executes a contract
    let [<When>] ``executing (.*) on (.*) returning (.*)`` contractLabel inputTxLabel outputTxLabel =
        executeContract contractLabel inputTxLabel outputTxLabel "" None None

    // Executes a contract with additional arguments
    let [<When>] ``executing (.*) on (.*) returning (.*) with`` contractLabel inputTxLabel outputTxLabel (table: Table) =
        let mutable command = String.Empty
        let mutable messageBody = None
        let mutable sender = None

        for row in table.Rows do
            let value = row.Item(1)
            match row.Item(0) with
            | "Command" -> command <- value
            | "Sender" -> sender <- getSender value
            | "MessageBody" ->
                messageBody <-
                    match tryFindData value with
                    | Some data -> Some data
                    | _ -> failwithf "cannot resolve data: %A" value
            | "ReturnAddress" ->
                let _, publicKey = initKey value
                match TestWallet.addReturnAddressToData publicKey messageBody with
                | Ok messageBody' -> messageBody <- messageBody'
                | Error error -> failwithf "error initializing data with return address: %A" error
            | other -> failwithf "unexpected execute option %A" other

        executeContract contractLabel inputTxLabel outputTxLabel command sender messageBody

    // Adding a single output
    // or adding a change output for an asset
    let [<Given>] ``(.*) locks (.*) (.*) to (.*)`` txLabel amount asset keyLabel =
        let tx = initTx txLabel

        let amount =
            if amount = "change" then
                let foldOutputs =
                    List.fold (fun amount (output:Output) ->
                        amount +
                        if output.spend.asset = getAsset asset then
                            output.spend.amount
                        else
                            0UL)
                let inputsTotal =
                    match UtxoSet.tryGetOutputs (getUTXO session) state.memoryState.utxoSet tx.inputs with
                    | Some outputs -> foldOutputs 0UL outputs
                    | None -> failwithf "unable to get utxos for tx %A" txLabel
                let outputsTotal = foldOutputs 0UL tx.outputs
                inputsTotal - outputsTotal
            else
                getAmount asset (UInt64.Parse amount)

        let output = getOutput amount asset keyLabel
        { tx with outputs = Infrastructure.List.add output tx.outputs }
        |> updateTx txLabel
        |> ignore

    // Adding contract activation outputs (ActivationSacrifice and Fee)
    let [<Given>] ``(.*) activates (.*) for (.*) blocks`` txLabel contractLabel blocks =
        let tx = initTx txLabel

        let _, contract = getContractRecord contractLabel
        let codeLength = String.length contract.code |> uint64
        let activationFee = contract.queries * rlimit |> uint64
        let activationSacrifice = chain.sacrificePerByteBlock * codeLength * (uint64 blocks)

        let outputs =
            [ { spend = { amount = activationSacrifice; asset = Asset.Zen }; lock = ActivationSacrifice }
              { spend = { amount = activationFee;       asset = Asset.Zen }; lock = Fee } ]

        { tx with outputs = tx.outputs @ outputs; contract = Some (V0 contract) }
        |> updateTx txLabel
        |> ignore

    let addInput txLabel refTxLabel index =
        let refTx = findTx refTxLabel
        let outpoint = Outpoint { txHash = Transaction.hash refTx; index = index }
        let tx = initTx txLabel
        { tx with inputs = Infrastructure.List.add outpoint tx.inputs }
        |> updateTx txLabel
        |> ignore

    // Adds an outpoint to a tx
    let [<Given>] ``(.*) has the input (.*) index (.*)`` txLabel refTxLabel index =
        addInput txLabel refTxLabel index

    // Adds outpoint(s) to a tx
    let [<Given>] ``(.*) has inputs`` txLabel (table: Table) =
        for row in table.Rows do
            let refTxLabel = row.GetString "Tx"
            let index = row.GetInt64 "Index" |> uint32
            addInput txLabel refTxLabel index

    let constructData dataType value =
        match dataType with
        | Regex @"(.+) (.+)" [ dataType; collectionType ] ->
            getDataCollection collectionType dataType value
        | _ ->
            getData dataType value

    // Defines a data
    let [<Given>] ``(.*) is (?:a|an) (.*) of (.*)`` dataLabel dataType value =
        constructData dataType value
        |> initData dataLabel

    // Defines a data dictionaty
    let [<Given>] ``(.*) is a dictionary of`` dataLabel (table: Table) =
        let mutable map = Map.empty

        for row in table.Rows do
            let key = row.GetString "Key"
            let dataType = row.GetString "Type"
            let value = row.GetString "Value"
            let data =
                if dataType = "dict" then
                    match tryFindData key with
                    | Some data -> data
                    | None -> failwithf "missing dictionary data %A" key
                else
                    constructData dataType value
            map <- Map.add (fsToFstString key) data map

        (map, Map.count map |> uint32)
        |> Zen.Types.Data.Dict
        |> Zen.Types.Data.Collection
        |> initData dataLabel

    // Signes a tx
    let [<Given>] ``(.*) is signed with (.*)`` txLabel keyLabels =
        let tx = findTx txLabel
        let keyPairs =
            keyLabels
            |> split
            |> List.map findKey
        let tx = Transaction.sign keyPairs tx
        updateTx txLabel tx
        |> ignore

    // Signes a tx
    let [<When>] ``signing (.*) with (.*)`` txLabel keyLabels =
        let tx = findTx txLabel
        let keyPairs =
            keyLabels
            |> split
            |> List.map findKey
        let tx = Transaction.sign keyPairs tx
        updateTx txLabel tx
        |> ignore

    // Initializes a chain with a genesis block
    let [<Given>] ``genesis has (.*)`` rootTxLabels =
        let rootTxs =
            rootTxLabels
            |> split
            |> List.map findTx

        let genesisBlock = Block.createGenesis chain rootTxs (0UL,0UL)
        let genesisHash = Block.hash genesisBlock.header

        testingState <- { testingState with blocks = Map.add "genesis" genesisBlock.header testingState.blocks }

        let chain = { chain with genesisHash = genesisHash }
        let state' = { state with tipState = { state.tipState with ema = EMA.create chain }}

        let events, state' =
            BlockHandler.validateBlock chain session.context.contractPath session timestamp None genesisBlock false state'
            |> Infrastructure.Writer.unwrap

        events |> should contain (EffectsWriter.EventEffect (BlockAdded (genesisHash, genesisBlock)))
        events |> should contain (EffectsWriter.EventEffect (TipChanged (genesisBlock.header)))

        state <- state'

//        printfn "%A" state
//    // Initializes a chain with a genesis block
//    let [<Given>] ``genesis has (.*) and uses (.*) index (.*) with key (.*) to activate (.*) for (.*) blocks`` rootTxLabels refTxLabel index key contractLabel blocks =
//        let rootTxs =
//            rootTxLabels
//            |> split
//            |> List.map findTx
//
//        let createContractRecord code =
//            result {
//                let! hints = Contract.recordHints code
//                let! queries = Infrastructure.ZFStar.totalQueries hints
//
//                return
//                    (Contract.makeContractId Version0 code,
//                        {   code = code
//                            hints = hints
//                            rlimit = rlimit
//                            queries = queries })
//            }

//        let getContractActivatingTx contract numberOfBlocks =
//            let refTx = findTx refTxLabel
//            let outpoint = { txHash = Transaction.hash refTx; index = index }
//
//            let output =
//                match UtxoSet.tryGetOutput (getUTXO session) state.memoryState.utxoSet outpoint with
//                | Some output -> output
//                | None -> failwithf "unable to get utxos for activating contract %A" contractLabel
//
//            let codeLength = String.length contract.code |> uint64
//            let activationFee = contract.queries * rlimit |> uint64
//            let activationSacrifice = chain.sacrificePerByteBlock * codeLength * (uint64 numberOfBlocks)
//
//            let changeAddress = PK Hash.zero
//            let outputs =
//                [
//                    { spend = { amount = activationSacrifice;
//                      asset = Asset.Zen }
//                      lock = ActivationSacrifice }
//                    { spend = { amount = activationFee;
//                      asset = Asset.Zen }
//                      lock = Fee }
//                    { spend = { amount = output.spend.amount - activationSacrifice - activationFee;
//                      asset = Asset.Zen }
//                      lock = changeAddress }
//                ]
//
//            Transaction.sign
//                [ findKey key ]
//                {
//                    version = Version0
//                    inputs = [ Outpoint outpoint ]
//                    outputs = outputs
//                    witnesses = []
//                    contract = Some (V0 contract)
//                }
//
//        let contract = createContractRecord "" |> Infrastructure.Result.get
//
//        let contractActivatingTx = getContractActivatingTx contract blocks
//
//        let genesisBlock = Block.createGenesis chain (rootTxs (* @ contractActivatingTx *) ) (0UL,0UL)
//        let genesisHash = Block.hash genesisBlock.header
//
//        testingState <- { testingState with blocks = Map.add "genesis" genesisBlock.header testingState.blocks }
//
//        let chain = { chain with genesisHash = genesisHash }
//        let state' = { state with tipState = { state.tipState with ema = EMA.create chain }}
//
//        let events, state' =
//            BlockHandler.validateBlock chain session.context.contractPath session timestamp None genesisBlock false state'
//            |> Infrastructure.Writer.unwrap
//
//        events |> should contain (EffectsWriter.EventEffect (BlockAdded (genesisHash, genesisBlock)))
//        events |> should contain (EffectsWriter.EventEffect (TipChanged (genesisBlock.header)))
//
//        state <- state'

    let extendChain newBlockLabel txLabels parentBlockLabel =
        let txs =
            txLabels
            |> split
            |> List.map findTx

        let parent =
            if parentBlockLabel = "tip" then
                state.tipState.tip.header
            else
                match Map.tryFind parentBlockLabel testingState.blocks with
                | Some block -> block
                | None -> failwithf "could not resolve parent block %A" parentBlockLabel

        let mempool = List.fold (fun mempool tx -> MemPool.add (Transaction.hash tx) tx mempool) MemPool.empty txs
        let state' = { state with memoryState = { state.memoryState with mempool = mempool } }
        let memState, validatedTransactions = BlockTemplateBuilder.makeTransactionList chain session state' timestamp
        let block = Block.createTemplate chain parent timestamp state.tipState.ema memState.activeContractSet validatedTransactions Hash.zero

        match newBlockLabel with
        | Some label ->
            testingState <- { testingState with blocks = Map.add label block.header testingState.blocks }
        | _ -> ()

        let events, state' =
            BlockHandler.validateBlock chain session.context.contractPath session timestamp None block false state
            |> Infrastructure.Writer.unwrap

      //  events |> should contain (EffectsWriter.EventEffect (BlockAdded (Block.hash block.header, block)))

      //  events |> should contain (EffectsWriter.EventEffect (BlockAdded (Block.hash block.header, block)))

//        if parentBlockLabel = "tip" then
//            try
//                events |> should contain (EffectsWriter.EventEffect (TipChanged (block.header)))
//            with _ ->
//                printfn "failed to validate %A" newBlockLabel

        state <- state'
        block

    // Extend a chain
    let [<When>] ``validating a block containing (.*) extending (.*)`` txLabels parentBlockLabel =
        extendChain None txLabels parentBlockLabel

    // Extend a chain (using block label)
    let [<When>] ``validating block (.*) containing (.*) extending (.*)`` newBlockLabel txLabels parentBlockLabel =
        extendChain (Some newBlockLabel) txLabels parentBlockLabel

    // Extend a chain (using block lable, empty block)
    let [<When>] ``validating an empty block (.*) extending (.*)`` newBlockLabel parentBlockLabel =
        extendChain (Some newBlockLabel) "" parentBlockLabel

    // Checks that tx passes in-context validation
    let [<Then>] ``(.*) should pass validation`` txLabel =
        let tx = findTx txLabel
        let chainParams = Chain.getChainParameters Chain.Test

        let acs =
            Map.fold (fun acs contractId contract -> ActiveContractSet.add contractId contract acs) ActiveContractSet.empty contractExecutionCache

        match TransactionValidation.validateInContext
            chainParams
            (getUTXO session)
            contractPath
            1ul
            1_000_000UL
            acs
            Map.empty
            state.memoryState.utxoSet
            (getContractState session)
            ContractStates.asDatabase
            (Transaction.hash tx)
            tx with
        | Ok _ -> ()
        | Error e -> failwithf "Validation result: %A" e

    //Checks that a tx is locking an asset to an address or a contract
    let [<Then>] ``(.*) should lock (.*) (.*) to (.*)`` txLabel (expected:uint64) asset keyLabel =
        let tx = findTx txLabel
        let asset = getAsset asset

        let stepDown asset amount =
            if asset = Asset.Zen then
                amount / 100_000_000UL
            else
                amount

        expected ?=
            List.fold (fun state { lock = lock; spend = spend } ->
                if lock = getLock keyLabel && spend.asset = asset then
                    state + (stepDown spend.asset spend.amount)
                else
                    state) 0UL tx.outputs

    // Checks a contract's state
    let [<Then>] ``state of (.*) should be (.*) of (.*)`` contractLabel dataType value =
        let expected =
            constructData dataType value
            |> Some

        match tryFindContract contractLabel with
        | Some (contractId, _) ->
            match ActiveContractSet.tryFind contractId state.memoryState.activeContractSet with
            | Some contract ->
                let actual = ContractStates.tryFind contract state.memoryState.contractStates
                expected ?= actual
            | None -> failwithf "contract %A not found in ACS" contractLabel
        | None -> failwith "contract not resolved"

    // Checks the tip
    let [<Then>] ``tip should be (.*)`` blockLabel =
        let expected = findBlock blockLabel

        let actual = state.tipState.tip.header

        expected ?= actual

    // Asserts that a contract is active
    let [<Then>] ``(.*) should be active for (.*) blocks`` contractLabel (blocks:uint32) =
        let contractId, _  = tryFindContract contractLabel |> Option.get
        match ActiveContractSet.tryFind contractId state.tipState.activeContractSet with
        | Some contract -> blocks ?= contract.expiry - state.tipState.tip.header.blockNumber
        | None -> failwithf "expected contract to be activated"

    // Asserts that a contract is not active
    let [<Then>] ``(.*) should not be active`` contractLabel =
        let contractId, _  = tryFindContract contractLabel |> Option.get
        match ActiveContractSet.tryFind contractId state.tipState.activeContractSet with
        | Some _ -> failwithf "expected contract to not be activated"
        | None -> ()
