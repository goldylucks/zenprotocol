module Network.Serialization

open Network.Types
open Consensus.Serialization
open Consensus.Serialization.Serialization
open Infrastructure.Timestamp

open FsNetMQ.Stream
open FsNetMQ.Stream.Reader

module Address =

    let write ops ((address:string),(timestamp:Timestamp)) =
        let bytes = System.Text.Encoding.ASCII.GetBytes address

        ops.writeNumber4 (bytes.Length |> uint32)
        >> ops.writeBytes bytes bytes.Length
        >> ops.writeNumber8 (timestamp |> uint64)

    let read = reader {
        let! length = readNumber4
        let! address = readBytes (length|>int)
        let! timestamp = readNumber8

        let address = System.Text.Encoding.ASCII.GetString(address)
        let timestamp = timestamp
        return address,timestamp
    }

    let serialize address =
        write counters address 0ul
        |> int32
        |> create
        |> write serializers address
        |> getBuffer

module Addresses =
    let serialize addresses =
        Seq.writeBody counters Address.write addresses 0ul
        |> int32
        |> create
        |> Seq.writeBody serializers Address.write addresses
        |> getBuffer

    let deserialize count bytes =
        Stream (bytes, 0)
        |> run (Seq.readBody count Address.read)
        |> Option.map List.ofSeq


