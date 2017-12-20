module Network.Tests.MessageTests

open NUnit.Framework
open FsUnit
open FsNetMQ
open Network.Message


[<Test>]
let ``send and recv Hello``() =
    let msg = Hello {
        network = 123ul;
        version = 123ul;
    }

    use server = Socket.dealer ()
    Socket.bind server "inproc://Hello.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://Hello.test"

    Network.Message.send server msg

    let msg' = Network.Message.recv client

    msg' |> should equal (Some msg)

[<Test>]
let ``Hello size fits stream ``() =
    let hello:Hello = {
        network = 123ul;
        version = 123ul;
    }

    let messageSize = Hello.getMessageSize hello

    let stream =
        Stream.create messageSize
        |> Hello.write hello

    let offset = Stream.getOffset stream

    messageSize |> should equal offset

[<Test>]
let ``send and recv HelloAck``() =
    let msg = HelloAck {
        network = 123ul;
        version = 123ul;
    }

    use server = Socket.dealer ()
    Socket.bind server "inproc://HelloAck.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://HelloAck.test"

    Network.Message.send server msg

    let msg' = Network.Message.recv client

    msg' |> should equal (Some msg)

[<Test>]
let ``HelloAck size fits stream ``() =
    let helloack:HelloAck = {
        network = 123ul;
        version = 123ul;
    }

    let messageSize = HelloAck.getMessageSize helloack

    let stream =
        Stream.create messageSize
        |> HelloAck.write helloack

    let offset = Stream.getOffset stream

    messageSize |> should equal offset

[<Test>]
let ``send and recv Ping``() =
    let msg = Ping 123ul

    use server = Socket.dealer ()
    Socket.bind server "inproc://Ping.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://Ping.test"

    Network.Message.send server msg

    let msg' = Network.Message.recv client

    msg' |> should equal (Some msg)

[<Test>]
let ``Ping size fits stream ``() =
    let ping:Ping =
        123ul

    let messageSize = Ping.getMessageSize ping

    let stream =
        Stream.create messageSize
        |> Ping.write ping

    let offset = Stream.getOffset stream

    messageSize |> should equal offset

[<Test>]
let ``send and recv Pong``() =
    let msg = Pong 123ul

    use server = Socket.dealer ()
    Socket.bind server "inproc://Pong.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://Pong.test"

    Network.Message.send server msg

    let msg' = Network.Message.recv client

    msg' |> should equal (Some msg)

[<Test>]
let ``Pong size fits stream ``() =
    let pong:Pong =
        123ul

    let messageSize = Pong.getMessageSize pong

    let stream =
        Stream.create messageSize
        |> Pong.write pong

    let offset = Stream.getOffset stream

    messageSize |> should equal offset

[<Test>]
let ``send and recv Transaction``() =
    let msg = Transaction ("Captcha Diem"B)

    use server = Socket.dealer ()
    Socket.bind server "inproc://Transaction.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://Transaction.test"

    Network.Message.send server msg

    let msg' = Network.Message.recv client

    msg' |> should equal (Some msg)

[<Test>]
let ``Transaction size fits stream ``() =
    let transaction:Transaction =
        "Captcha Diem"B

    let messageSize = Transaction.getMessageSize transaction

    let stream =
        Stream.create messageSize
        |> Transaction.write transaction

    let offset = Stream.getOffset stream

    messageSize |> should equal offset

[<Test>]
let ``send and recv Address``() =
    let msg = Address "Life is short but Now lasts for ever"

    use server = Socket.dealer ()
    Socket.bind server "inproc://Address.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://Address.test"

    Network.Message.send server msg

    let msg' = Network.Message.recv client

    msg' |> should equal (Some msg)

[<Test>]
let ``Address size fits stream ``() =
    let address:Address =
        "Life is short but Now lasts for ever"

    let messageSize = Address.getMessageSize address

    let stream =
        Stream.create messageSize
        |> Address.write address

    let offset = Stream.getOffset stream

    messageSize |> should equal offset

[<Test>]
let ``send and recv UnknownPeer``() =
    let msg = UnknownPeer

    use server = Socket.dealer ()
    Socket.bind server "inproc://UnknownPeer.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://UnknownPeer.test"

    Network.Message.send server msg

    let msg' = Network.Message.recv client

    msg' |> should equal (Some msg)


[<Test>]
let ``send and recv UnknownMessage``() =
    let msg = UnknownMessage 123uy

    use server = Socket.dealer ()
    Socket.bind server "inproc://UnknownMessage.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://UnknownMessage.test"

    Network.Message.send server msg

    let msg' = Network.Message.recv client

    msg' |> should equal (Some msg)

[<Test>]
let ``UnknownMessage size fits stream ``() =
    let unknownmessage:UnknownMessage =
        123uy

    let messageSize = UnknownMessage.getMessageSize unknownmessage

    let stream =
        Stream.create messageSize
        |> UnknownMessage.write unknownmessage

    let offset = Stream.getOffset stream

    messageSize |> should equal offset

[<Test>]
let ``malformed message return None``() =
    use server = Socket.dealer ()
    Socket.bind server "inproc://NetworkMessage.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://NetworkMessage.test"

    Frame.send server "hello world"B

    let msg = Network.Message.recv client
    msg |> should equal None
 