using System.Numerics;
using System.Runtime.InteropServices;

namespace Atom.Compilers.JavaScript;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct JavaScriptRuntimeValue(
    JavaScriptRuntimeValueKind Kind,
    object? BoxedValue)
{
    internal static JavaScriptRuntimeValue Null => new(JavaScriptRuntimeValueKind.Null, BoxedValue: null);
    internal static JavaScriptRuntimeValue Undefined => new(JavaScriptRuntimeValueKind.Undefined, BoxedValue: null);

    internal static JavaScriptRuntimeValue FromBoolean(bool value)
        => new(JavaScriptRuntimeValueKind.Boolean, value);

    internal static JavaScriptRuntimeValue FromNumber(double value)
        => new(JavaScriptRuntimeValueKind.Number, value);

    internal static JavaScriptRuntimeValue FromBigInt(BigInteger value)
        => new(JavaScriptRuntimeValueKind.BigInt, value);

    internal static JavaScriptRuntimeValue FromSymbol(JavaScriptRuntimeSymbol symbol)
        => new(JavaScriptRuntimeValueKind.Symbol, symbol);

    internal static JavaScriptRuntimeValue FromArray(JavaScriptRuntimeArray array)
        => new(JavaScriptRuntimeValueKind.Array, array);

    internal static JavaScriptRuntimeValue FromArrayBuffer(JavaScriptRuntimeArrayBuffer value)
        => new(JavaScriptRuntimeValueKind.ArrayBuffer, value);

    internal static JavaScriptRuntimeValue FromSharedArrayBuffer(JavaScriptRuntimeSharedArrayBuffer value)
        => new(JavaScriptRuntimeValueKind.SharedArrayBuffer, value);

    internal static JavaScriptRuntimeValue FromDataView(JavaScriptRuntimeDataView value)
        => new(JavaScriptRuntimeValueKind.DataView, value);

    internal static JavaScriptRuntimeValue FromTypedArray(JavaScriptRuntimeTypedArray value)
        => new(JavaScriptRuntimeValueKind.TypedArray, value);

    internal static JavaScriptRuntimeValue FromInt8Array(JavaScriptRuntimeInt8Array value)
        => new(JavaScriptRuntimeValueKind.Int8Array, value);

    internal static JavaScriptRuntimeValue FromUint8Array(JavaScriptRuntimeUint8Array value)
        => new(JavaScriptRuntimeValueKind.Uint8Array, value);

    internal static JavaScriptRuntimeValue FromUint8ClampedArray(JavaScriptRuntimeUint8ClampedArray value)
        => new(JavaScriptRuntimeValueKind.Uint8ClampedArray, value);

    internal static JavaScriptRuntimeValue FromUint16Array(JavaScriptRuntimeUint16Array value)
        => new(JavaScriptRuntimeValueKind.Uint16Array, value);

    internal static JavaScriptRuntimeValue FromInt16Array(JavaScriptRuntimeInt16Array value)
        => new(JavaScriptRuntimeValueKind.Int16Array, value);

    internal static JavaScriptRuntimeValue FromInt32Array(JavaScriptRuntimeInt32Array value)
        => new(JavaScriptRuntimeValueKind.Int32Array, value);

    internal static JavaScriptRuntimeValue FromUint32Array(JavaScriptRuntimeUint32Array value)
        => new(JavaScriptRuntimeValueKind.Uint32Array, value);

    internal static JavaScriptRuntimeValue FromFloat32Array(JavaScriptRuntimeFloat32Array value)
        => new(JavaScriptRuntimeValueKind.Float32Array, value);

    internal static JavaScriptRuntimeValue FromFloat64Array(JavaScriptRuntimeFloat64Array value)
        => new(JavaScriptRuntimeValueKind.Float64Array, value);

    internal static JavaScriptRuntimeValue FromBigInt64Array(JavaScriptRuntimeBigInt64Array value)
        => new(JavaScriptRuntimeValueKind.BigInt64Array, value);

    internal static JavaScriptRuntimeValue FromBigUint64Array(JavaScriptRuntimeBigUint64Array value)
        => new(JavaScriptRuntimeValueKind.BigUint64Array, value);

    internal static JavaScriptRuntimeValue FromAtomics(JavaScriptRuntimeAtomics value)
        => new(JavaScriptRuntimeValueKind.Atomics, value);

    internal static JavaScriptRuntimeValue FromProxy(JavaScriptRuntimeProxy value)
        => new(JavaScriptRuntimeValueKind.Proxy, value);

    internal static JavaScriptRuntimeValue FromReflect(JavaScriptRuntimeReflect value)
        => new(JavaScriptRuntimeValueKind.Reflect, value);

    internal static JavaScriptRuntimeValue FromMath(JavaScriptRuntimeMath value)
        => new(JavaScriptRuntimeValueKind.Math, value);

    internal static JavaScriptRuntimeValue FromJson(JavaScriptRuntimeJson value)
        => new(JavaScriptRuntimeValueKind.Json, value);

    internal static JavaScriptRuntimeValue FromObject(JavaScriptRuntimeObject value)
        => new(JavaScriptRuntimeValueKind.Object, value);

    internal static JavaScriptRuntimeValue FromFunction(JavaScriptRuntimeFunction value)
        => new(JavaScriptRuntimeValueKind.Function, value);

    internal static JavaScriptRuntimeValue FromPromise(JavaScriptRuntimePromise value)
        => new(JavaScriptRuntimeValueKind.Promise, value);

    internal static JavaScriptRuntimeValue FromSet(JavaScriptRuntimeSet value)
        => new(JavaScriptRuntimeValueKind.Set, value);

    internal static JavaScriptRuntimeValue FromMap(JavaScriptRuntimeMap value)
        => new(JavaScriptRuntimeValueKind.Map, value);

    internal static JavaScriptRuntimeValue FromWeakMap(JavaScriptRuntimeWeakMap value)
        => new(JavaScriptRuntimeValueKind.WeakMap, value);

    internal static JavaScriptRuntimeValue FromWeakSet(JavaScriptRuntimeWeakSet value)
        => new(JavaScriptRuntimeValueKind.WeakSet, value);

    internal static JavaScriptRuntimeValue FromWeakRef(JavaScriptRuntimeWeakRef value)
        => new(JavaScriptRuntimeValueKind.WeakRef, value);

    internal static JavaScriptRuntimeValue FromFinalizationRegistry(JavaScriptRuntimeFinalizationRegistry value)
        => new(JavaScriptRuntimeValueKind.FinalizationRegistry, value);

    internal static JavaScriptRuntimeValue FromRegExp(JavaScriptRuntimeRegExp value)
        => new(JavaScriptRuntimeValueKind.RegExp, value);

    internal static JavaScriptRuntimeValue FromDate(JavaScriptRuntimeDate value)
        => new(JavaScriptRuntimeValueKind.Date, value);

    internal static JavaScriptRuntimeValue FromError(JavaScriptRuntimeError value)
        => new(JavaScriptRuntimeValueKind.Error, value);

    internal static JavaScriptRuntimeValue FromTypeError(JavaScriptRuntimeTypeError value)
        => new(JavaScriptRuntimeValueKind.TypeError, value);

    internal static JavaScriptRuntimeValue FromRangeError(JavaScriptRuntimeRangeError value)
        => new(JavaScriptRuntimeValueKind.RangeError, value);

    internal static JavaScriptRuntimeValue FromReferenceError(JavaScriptRuntimeReferenceError value)
        => new(JavaScriptRuntimeValueKind.ReferenceError, value);

    internal static JavaScriptRuntimeValue FromSyntaxError(JavaScriptRuntimeSyntaxError value)
        => new(JavaScriptRuntimeValueKind.SyntaxError, value);

    internal static JavaScriptRuntimeValue FromUriError(JavaScriptRuntimeUriError value)
        => new(JavaScriptRuntimeValueKind.UriError, value);

    internal static JavaScriptRuntimeValue FromEvalError(JavaScriptRuntimeEvalError value)
        => new(JavaScriptRuntimeValueKind.EvalError, value);

    internal static JavaScriptRuntimeValue FromAggregateError(JavaScriptRuntimeAggregateError value)
        => new(JavaScriptRuntimeValueKind.AggregateError, value);

    internal static JavaScriptRuntimeValue FromSuppressedError(JavaScriptRuntimeSuppressedError value)
        => new(JavaScriptRuntimeValueKind.SuppressedError, value);

    internal static JavaScriptRuntimeValue FromInternalError(JavaScriptRuntimeInternalError value)
        => new(JavaScriptRuntimeValueKind.InternalError, value);

    internal static JavaScriptRuntimeValue FromStackOverflowError(JavaScriptRuntimeStackOverflowError value)
        => new(JavaScriptRuntimeValueKind.StackOverflowError, value);

    internal static JavaScriptRuntimeValue FromTimeoutError(JavaScriptRuntimeTimeoutError value)
        => new(JavaScriptRuntimeValueKind.TimeoutError, value);

    internal static JavaScriptRuntimeValue FromMemoryLimitError(JavaScriptRuntimeMemoryLimitError value)
        => new(JavaScriptRuntimeValueKind.MemoryLimitError, value);

    internal static JavaScriptRuntimeValue FromCancellationError(JavaScriptRuntimeCancellationError value)
        => new(JavaScriptRuntimeValueKind.CancellationError, value);

    internal static JavaScriptRuntimeValue FromHostInteropError(JavaScriptRuntimeHostInteropError value)
        => new(JavaScriptRuntimeValueKind.HostInteropError, value);

    internal static JavaScriptRuntimeValue FromResourceExhaustedError(JavaScriptRuntimeResourceExhaustedError value)
        => new(JavaScriptRuntimeValueKind.ResourceExhaustedError, value);

    internal static JavaScriptRuntimeValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(JavaScriptRuntimeValueKind.String, value);
    }

    internal static JavaScriptRuntimeValue FromHostObject(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(JavaScriptRuntimeValueKind.HostObject, value);
    }
}