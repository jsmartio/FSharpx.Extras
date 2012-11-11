namespace FSharpx

open System
open System.Collections
open System.Collections.Generic
#if NET40
open System.Diagnostics.Contracts
#endif
open System.Runtime.CompilerServices
            
module Seq =
    /// <summary>
    /// Adds an index to a sequence
    /// </summary>
    /// <param name="a"></param>
    let inline index a = Seq.mapi tuple2 a

    /// <summary>
    /// Returns the first element (with its index) for which the given function returns true.
    /// Return None if no such element exists.
    /// </summary>
    /// <param name="pred">Predicate</param>
    /// <param name="l">Sequence</param>
    let tryFindWithIndex pred l =
        l |> index |> Seq.tryFind (fun (_,v) -> pred v)

    let inline lift2 f l1 l2 = 
        seq {
            for i in l1 do
                for j in l2 do
                    yield f i j }

    let foldMap (monoid: _ Monoid) f =
        Seq.fold (fun s e -> monoid.Combine(s, f e)) (monoid.Zero())
    
    /// Will iterate the current sequence until the given predicate is statisfied
    let iterBreak (f:'a -> bool) (seq:seq<_>) = 
        use en = seq.GetEnumerator() 
        let mutable run = true
        while en.MoveNext() && run do
            run <- f en.Current
    
    /// The same as Seq.average except will return None if the seq is empty
    let inline tryAverage (seq : seq<(^a)>) : ^a option =
        if not(Seq.isEmpty seq)
        then Some <| (Seq.average seq)
        else None
    
    /// Splits a sequences at the given index
    let splitAt n seq = (Seq.take n seq, Seq.skip n seq)
    
    /// Converts a streamReader into a seq yielding on each line
    let ofStreamReader (streamReader : System.IO.StreamReader) = 
         seq {  
                use sr = streamReader
                while not(sr.EndOfStream) do
                    yield sr.ReadLine()
             }
    
    /// Converts a Stream into a sequence of bytes
    let ofStreamByByte (stream: System.IO.Stream) =
        seq { while stream.Length <> stream.Position do
                let x = stream.ReadByte()
                if (int x) < 0 then ()
                else yield x }
    
    /// Converts a stream into a seq of byte[] where the array is of the length given
    let ofStreamByChunk chunkSize (stream: System.IO.Stream) =
        let buffer = Array.zeroCreate<byte> chunkSize
        seq { while stream.Length <> stream.Position do
                let bytesRead = stream.Read(buffer, 0, chunkSize)
                if bytesRead = 0 then ()
                else yield buffer }
    
    /// Creates a inifinte sequences of the given values
    let asCircular values = 
        let rec next () = 
            seq {
                for element in values do
                    yield element
                yield! next()
            }
        next()
    
    /// Creates a inifinte sequences of the given values, executing the given function everytime the given seq is exhusted
    let asCircularOnLoop f values = 
        let rec next () = 
            seq {
                for element in values do
                    yield element
                f()
                yield! next()
            }
        next()

    /// Creates a inifinte sequences of the given values returning None everytime the given seq is exhusted
    let asCircularWithBreak values = 
        let rec next () = 
            seq {
                for element in values do
                    yield Some(element)
                yield None
                yield! next()
            }
        next()
    
    /// Converts a System.Collections.IEnumerable to a seq 
    let ofEnumerable<'a> (a : System.Collections.IEnumerable) = 
         let enum = a.GetEnumerator()
         seq {
             while enum.MoveNext() do yield unbox<'a> enum.Current
         }
    
    /// The same as Seq.nth except returns None if the sequence is empty or does not have enough elements
    let tryNth index (source : seq<_>) = 
         let rec tryNth' index (e : System.Collections.Generic.IEnumerator<'a>) = 
             if not (e.MoveNext()) then None
             else if index < 0 then None
             else if index = 0 then Some(e.Current)
             else tryNth' (index-1) e
    
         use e = source.GetEnumerator()
         tryNth' index e
    
    /// The same as Seq.skip except returns None if the sequence is empty or does not have enough elements
    let trySkip count (source: seq<_>) =
        seq { use e = source.GetEnumerator() 
              for _ in 1 .. count do
                  if not (e.MoveNext()) then ()
              while e.MoveNext() do
                  yield e.Current }
    
    /// The same as Seq.take except returns None if the sequence is empty or does not have enough elements
    let tryTake count (source : seq<'T>)    = 
        if Unchecked.equals null source then invalidArg "source" "input seq cannot be null"
        if count < 0 then invalidArg "count" "count must be non negative"
        if count = 0 then Seq.empty else  
        seq { use e = source.GetEnumerator() 
              for _ in 0 .. count - 1 do
                  if (e.MoveNext()) 
                  then yield e.Current
                  else ()
             }
    
    /// Creates an inifinte sequence of the given value
    let repeat a = seq { while true do yield a }

    /// Contracts a seq selecting every n values
    let contract n (a : seq<_>) =
        let rec ctr' s vals' =
            match vals' |> trySkip (n - 1) |> List.ofSeq with
            | [] -> s
            | h :: t -> ctr' (h :: s) (t |> Seq.ofList)
        ctr' [] a |> List.rev |> Seq.ofList
    
    /// Merges to sequences using the given function to transform the elements for comparision
    let rec mergeBy f (a : seq<_>) (b : seq<_>) =
        seq {
            match a |> List.ofSeq, b |> List.ofSeq with
            | h :: t, h' :: t' when f(h) < f(h') ->
                yield h; yield! mergeBy f t b
            | h :: t, h' :: t' -> 
                yield h'; yield! mergeBy f a t'
            | h :: t, [] -> yield! a
            | [], h :: t -> yield! b
            | [], [] -> ()
        }

    /// Combines to sequences using the given function
    let rec combine f (a : seq<_>) (b : seq<_>) =
        seq {
            match a |> List.ofSeq, b |> List.ofSeq with
            | h :: t, h' :: t' -> 
                yield f h h'
                yield! combine f t t'
            | h :: t, [] -> yield! a
            | [], h :: t -> yield! b
            | [], [] -> ()
        }

    /// Merges two sequences by the default comparer for 'a
    let merge a b = mergeBy id a b

    /// Replicates each element in the seq n-times
    let grow n = Seq.collect (fun x -> (repeat x) |> Seq.take n)

    /// Pages the underlying sequence
    let page page pageSize (source : seq<_>) =
          source |> trySkip (page * pageSize) |> tryTake pageSize

    /// Returns an array of sliding windows of data drawn from the source array.
    /// Each window contains the n elements surrounding the current element.
    let centeredWindow n (source: _ seq) =    
        if n < 0 then invalidArg "n" "n must be a positive integer"
        let source' = Seq.toArray source
        let lastIndex = Array.length source' - 1
    
        let window i _ =
            let windowStartIndex = Math.Max(i - n, 0)
            let windowEndIndex = Math.Min(i + n, lastIndex)
            let arrSize = windowEndIndex - windowStartIndex + 1
            let target = Array.zeroCreate arrSize
            Array.blit source' windowStartIndex target 0 arrSize
            target |> Seq.ofArray

        Array.mapi window source' |> Seq.ofArray

    /// Calculates the central moving average for the array using n elements either side
    /// of the point where the mean is being calculated.
    let inline centralMovingAverage n (a:^t seq) = 
        a |> centeredWindow n |> Seq.map Seq.average
        
    /// Calculates the central moving average for the array of optional elements using n
    /// elements either side of the point where the mean is being calculated. If any of
    /// the optional elements in the averaging window are None then the average itself
    /// is None.
    let inline centralMovingAverageOfOption n (a:^t option array) = 
        a 
        |> centeredWindow n 
        |> Seq.map (fun window ->
                        if Seq.exists Option.isNone window
                        then None
                        else Some(Seq.averageBy Option.get window))
        

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Array = 
    let inline nth i arr = Array.get arr i
    let inline setAt i v arr = Array.set arr i v; arr

    let copyTo sourceStartIndx startIndx source target =
        let targetLength = (Array.length target)
        if startIndx < 0 || startIndx > targetLength - 1 then
            failwith "Start Index outside the bounds of the array"

        let sourceLength = (Array.length source)
        let elementsToCopy = 
                    if (targetLength - startIndx - sourceStartIndx) > sourceLength then
                        sourceLength
                    else
                       (targetLength - startIndx - sourceStartIndx)    

        Array.blit source sourceStartIndx target startIndx elementsToCopy
    
    let ofTuple (source : obj) : obj array = 
        Microsoft.FSharp.Reflection.FSharpValue.GetTupleFields source

    let toTuple (source : 'a array) : 't = 
        let elements = source |> Array.map (fun x -> x :> obj)
        Microsoft.FSharp.Reflection.FSharpValue.MakeTuple(elements, typeof<'t>) :?> 't

module List =
    /// Curried cons
    let inline cons hd tl = hd::tl
  
    let inline singleton x = [x]

    let inline lift2 f (l1: _ list) (l2: _ list) = 
        [ for i in l1 do
            for j in l2 do
                yield f i j ]

  
    let span pred l =
        let rec loop l cont =
            match l with
            | [] -> ([],[])
            | x::[] when pred x -> (cont l, [])
            | x::xs when not (pred x) -> (cont [], l)
            | x::xs when pred x -> loop xs (fun rest -> cont (x::rest))
            | _ -> failwith "Unrecognized pattern"
        loop l id
  
    let split pred l = span (not << pred) l
  
    let skipWhile pred l = span pred l |> snd
    let skipUntil pred l = split pred l |> snd
    let takeWhile pred l = span pred l |> fst
    let takeUntil pred l = split pred l |> fst
    
    let splitAt n l =
        let pred i = i >= n
        let rec loop i l cont =
            match l with
            | [] -> ([],[])
            | x::[] when not (pred i) -> (cont l, [])
            | x::xs when pred i -> (cont [], l)
            | x::xs when not (pred i) -> loop (i+1) xs (fun rest -> cont (x::rest))
            | _ -> failwith "Unrecognized pattern"
        loop 0 l id
  
    let skip n l = splitAt n l |> snd
    let take n l = splitAt n l |> fst

    let inline mapIf pred f =
        List.map (fun x -> if pred x then f x else x)

    /// Behaves like a combination of map and fold; 
    /// it applies a function to each element of a list, 
    /// passing an accumulating parameter from left to right, 
    /// and returning a final value of this accumulator together with the new list.
    let mapAccum f s l =
        let rec loop s l cont =
            match l with
            | [] -> cont (s, [])
            | x::xs ->
                let (s, y) = f s x
                loop s xs (fun (s,ys) -> cont (s, y::ys))
        loop s l id

    let foldMap (monoid: _ Monoid) f =
        List.fold (fun s e -> monoid.Combine(s, f e)) (monoid.Zero())

    /// List monoid
    let monoid<'a> =
        { new Monoid<'a list>() with
            override this.Zero() = []
            override this.Combine(a,b) = a @ b }

module Set =
    let monoid<'a when 'a : comparison> =
        { new Monoid<'a Set>() with
            override this.Zero() = Set.empty
            override this.Combine(a,b) = Set.union a b }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Dictionary =
    let tryFind key (d: IDictionary<_,_>) =
        match d.TryGetValue key with
        | true,v -> Some v
        | _ -> None

module Map =
    let spanWithKey pred map =
        map
        |> Map.fold (fun (l,r) k v ->
            match k,v with
            | (key, value) when pred key -> (l, r |> Map.add key value)
            | (key, value) when not (pred key) -> (l |> Map.add key value, r)
            | _ -> failwith "Unrecognized pattern"
        ) (Map.empty, Map.empty)

    let splitWithKey pred d = spanWithKey (not << pred) d

    /// <summary>
    /// <code>insertWith f key value mp</code> will insert the pair <code>(key, value)</code> into <code>mp</code> if <code>key</code> does not exist in the map. 
    /// If the key does exist, the function will insert <code>f new_value old_value</code>.
    /// </summary>
    let insertWith f key value map =
        match Map.tryFind key map with
        | Some oldValue -> map |> Map.add key (f value oldValue)
        | None -> map |> Map.add key value

    /// <summary>
    /// <code>update f k map</code> updates the value <code>x</code> at key <code>k</code> (if it is in the map). 
    /// If <code>f x</code> is <code>None</code>, the element is deleted. 
    /// If it is <code>Some y</code>, the key is bound to the new value <code>y</code>.
    /// </summary>
    let updateWith f key map =
        let inner v map =
            match f v with
            | Some value -> map |> Map.add key value
            | None -> map |> Map.remove key
        match Map.tryFind key map with
        | Some v -> inner v map
        | None -> map

    let valueList map = map |> Map.toList |> List.unzip |> snd

    let union (map1: Map<_,_>) (map2: Map<_,_>) = 
        Seq.fold (fun m (KeyValue(k,v)) -> Map.add k v m) map1 map2

            
    let monoid<'key, 'value when 'key : comparison> =
        { new Monoid<Map<'key, 'value>>() with
            override this.Zero() = Map.empty
            override this.Combine(a,b) = union a b }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
[<Extension>]
module NameValueCollection =
    open System.Collections.Specialized
    open System.Linq

    /// <summary>
    /// Returns a new <see cref="NameValueCollection"/> with the concatenation of two <see cref="NameValueCollection"/>s
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    [<Extension>]
    [<CompiledName("Concat")>]
    let concat a b = 
        let x = NameValueCollection()
        x.Add a
        x.Add b
        x

    /// <summary>
    /// In-place add of a key-value pair to a <see cref="NameValueCollection"/>
    /// </summary>
    /// <param name="x"></param>
    /// <param name="a"></param>
    /// <param name="b"></param>
    let inline addInPlace (x: NameValueCollection) (a,b) = x.Add(a,b)

    /// Adds an element to a copy of an existing NameValueCollection
    let add name value (x: NameValueCollection) =
        let r = NameValueCollection x
        r.Add(name,value)
        r

    /// <summary>
    /// Returns a <see cref="NameValueCollection"/> as an array of key-value pairs.
    /// Note that keys may be duplicated.
    /// </summary>
    /// <param name="a"></param>
    [<Extension>]
    [<CompiledName("ToArray")>]
    let toArray (a: NameValueCollection) =
        a.AllKeys
        |> Array.collect (fun k -> a.GetValues k |> Array.map (fun v -> k,v))

    /// <summary>
    /// Returns a <see cref="NameValueCollection"/> as a sequence of key-value pairs.
    /// Note that keys may be duplicated.
    /// </summary>
    /// <param name="a"></param>
    [<Extension>]
    [<CompiledName("ToEnumerable")>]
    let toSeq (a: NameValueCollection) =
        a.AllKeys
        |> Seq.collect (fun k -> a.GetValues k |> Seq.map (fun v -> k,v))

    /// <summary>
    /// Returns a <see cref="NameValueCollection"/> as a list of key-value pairs.
    /// Note that keys may be duplicated.
    /// </summary>
    /// <param name="a"></param>
    let inline toList a = toSeq a |> Seq.toList

    /// <summary>
    /// Creates a <see cref="NameValueCollection"/> from a list of key-value pairs
    /// </summary>
    /// <param name="l"></param>
    let fromSeq l =
        let x = NameValueCollection()
        Seq.iter (addInPlace x) l
        x

    [<Extension>]
    [<CompiledName("ToLookup")>]
    let toLookup a =
        let s = toSeq a
        s.ToLookup(fst, snd)

    [<Extension>]
    [<CompiledName("AsDictionary")>]
    let asDictionary (x: NameValueCollection) =
        let notimpl() = raise <| NotImplementedException()
        let getEnumerator() =
            let enum = x.GetEnumerator()
            let wrapElem (o: obj) = 
                let key = o :?> string
                let values = x.GetValues key
                KeyValuePair(key,values)
            { new IEnumerator<KeyValuePair<string,string[]>> with
                member e.Current = wrapElem enum.Current
                member e.MoveNext() = enum.MoveNext()
                member e.Reset() = enum.Reset()
                member e.Dispose() = ()
                member e.Current = box (wrapElem enum.Current) }
        { new IDictionary<string,string[]> with
            member d.Count = x.Count
            member d.IsReadOnly = false 
            member d.Item 
                with get k = 
                    let v = x.GetValues k
                    if v = null
                        then raise <| KeyNotFoundException(sprintf "Key '%s' not found" k)
                        else v
                and set k v =
                    x.Remove k
                    for i in v do
                        x.Add(k,i)
            member d.Keys = upcast ResizeArray<string>(x.Keys |> Seq.cast)
            member d.Values = 
                let values = ResizeArray<string[]>()
                for i in 0..x.Count-1 do
                    values.Add(x.GetValues i)
                upcast values
            member d.Add v = d.Add(v.Key, v.Value)
            member d.Add(key,value) = 
                if key = null
                    then raise <| ArgumentNullException("key")
                if d.ContainsKey key
                    then raise <| ArgumentException(sprintf "Duplicate key '%s'" key, "key")
                d.[key] <- value
            member d.Clear() = x.Clear()
            member d.Contains item = x.GetValues(item.Key) = item.Value
            member d.ContainsKey key = x.[key] <> null
            member d.CopyTo(array,arrayIndex) = notimpl()
            member d.GetEnumerator() = getEnumerator()
            member d.GetEnumerator() = getEnumerator() :> IEnumerator
            member d.Remove (item: KeyValuePair<string,string[]>) = 
                if d.Contains item then
                    x.Remove item.Key
                    true
                else
                    false
            member d.Remove (key: string) = 
                let exists = d.ContainsKey key
                x.Remove key
                exists
            member d.TryGetValue(key: string, value: byref<string[]>) = 
                if d.ContainsKey key then
                    value <- d.[key]
                    true
                else false
            }

    [<Extension>]
    [<CompiledName("AsReadonlyDictionary")>]
    let asReadonlyDictionary x =
        let a = asDictionary x
        let notSupported() = raise <| NotSupportedException("Readonly dictionary")
        { new IDictionary<string,string[]> with
            member d.Count = a.Count
            member d.IsReadOnly = true
            member d.Item 
                with get k = a.[k]
                and set k v = notSupported()
            member d.Keys = a.Keys
            member d.Values = a.Values
            member d.Add v = notSupported()
            member d.Add(key,value) = notSupported()
            member d.Clear() = notSupported()
            member d.Contains item = a.Contains item
            member d.ContainsKey key = a.ContainsKey key
            member d.CopyTo(array,arrayIndex) = a.CopyTo(array,arrayIndex)
            member d.GetEnumerator() = a.GetEnumerator()
            member d.GetEnumerator() = a.GetEnumerator() :> IEnumerator
            member d.Remove (item: KeyValuePair<string,string[]>) = notSupported(); false
            member d.Remove (key: string) = notSupported(); false
            member d.TryGetValue(key: string, value: byref<string[]>) = a.TryGetValue(key, ref value)
        }                

    [<Extension>]
    [<CompiledName("AsLookup")>]
    let asLookup (this: NameValueCollection) =
        let getEnumerator() = 
            let e = this.GetEnumerator()
            let wrapElem (o: obj) = 
                let key = o :?> string
                let values = this.GetValues key :> seq<string>
                { new IGrouping<string,string> with
                    member x.Key = key
                    member x.GetEnumerator() = values.GetEnumerator()
                    member x.GetEnumerator() = values.GetEnumerator() :> IEnumerator }
  
            { new IEnumerator<IGrouping<string,string>> with
                member x.Current = wrapElem e.Current
                member x.MoveNext() = e.MoveNext()
                member x.Reset() = e.Reset()
                member x.Dispose() = ()
                member x.Current = box (wrapElem e.Current) }
                      
        { new ILookup<string,string> with
            member x.Count = this.Count
            member x.Item 
                with get key = 
                    match this.GetValues key with
                    | null -> Seq.empty
                    | a -> upcast a
            member x.Contains key = this.Get key <> null
            member x.GetEnumerator() = getEnumerator()
            member x.GetEnumerator() = getEnumerator() :> IEnumerator }
