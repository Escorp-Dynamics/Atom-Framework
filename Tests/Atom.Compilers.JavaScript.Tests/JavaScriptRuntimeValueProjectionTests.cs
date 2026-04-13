using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;

namespace Atom.Compilers.JavaScript.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptRuntimeValueProjectionTests
{
    [Test]
    public void RuntimeValueProjectionReturnsNullForNullValueKindTest()
    {
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.Null);

        Assert.That(projected, Is.Null);
    }

    [Test]
    public void RuntimeValueProjectionReturnsNullForUndefinedValueKindTest()
    {
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.Undefined);

        Assert.That(projected, Is.Null);
    }

    [Test]
    public void RuntimeValueProjectionReturnsSameHostObjectInstanceTest()
    {
        var hostObject = new object();

        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromHostObject(hostObject));

        Assert.That(projected, Is.SameAs(hostObject));
    }

    [Test]
    public void RuntimeValueProjectionRejectsHostObjectForEcmaScriptSpecificationTest()
    {
        var hostObject = new object();

        Assert.That(
            () => JavaScriptRuntimeValueProjection.Project(
                JavaScriptRuntimeValue.FromHostObject(hostObject),
                JavaScriptRuntimeSpecification.ECMAScript),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript runtime value kind 'HostObject' is not available when JavaScriptRuntimeSpecification.ECMAScript is selected."));
    }

    [Test]
    public void RuntimeValueProjectionReturnsSameStringValueTest()
    {
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromString("atom"));

        Assert.That(projected, Is.EqualTo("atom"));
    }

    [Test]
    public void RuntimeValueProjectionReturnsBooleanValueTest()
    {
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromBoolean(true));

        Assert.That(projected, Is.True);
    }

    [Test]
    public void RuntimeValueProjectionReturnsNumberValueTest()
    {
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromNumber(42.5d));

        Assert.That(projected, Is.EqualTo(42.5d));
    }

    [Test]
    public void RuntimeValueProjectionReturnsBigIntValueTest()
    {
        var value = BigInteger.Parse("9007199254740993");
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromBigInt(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueSymbolContractTest()
    {
        var symbol = new JavaScriptRuntimeSymbol("token");
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromSymbol(symbol));

        Assert.That(projected, Is.EqualTo(symbol));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueArrayContractTest()
    {
        var array = new JavaScriptRuntimeArray(ImmutableArray.Create(
            JavaScriptRuntimeValue.FromBoolean(true),
            JavaScriptRuntimeValue.FromNumber(1d),
            JavaScriptRuntimeValue.FromString("atom")));

        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromArray(array));

        Assert.That(projected, Is.EqualTo(array));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueArrayBufferContractTest()
    {
        var value = new JavaScriptRuntimeArrayBuffer(ByteLength: 4096, IsResizable: false);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromArrayBuffer(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueSharedArrayBufferContractTest()
    {
        var value = new JavaScriptRuntimeSharedArrayBuffer(ByteLength: 4096, IsGrowable: true);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromSharedArrayBuffer(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueDataViewContractTest()
    {
        var value = new JavaScriptRuntimeDataView(ByteOffset: 16, ByteLength: 256);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromDataView(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueUint8ArrayContractTest()
    {
        var value = new JavaScriptRuntimeUint8Array(Length: 256, ByteOffset: 0);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromUint8Array(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueUint8ClampedArrayContractTest()
    {
        var value = new JavaScriptRuntimeUint8ClampedArray(Length: 96, ByteOffset: 12);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromUint8ClampedArray(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueUint16ArrayContractTest()
    {
        var value = new JavaScriptRuntimeUint16Array(Length: 128, ByteOffset: 8);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromUint16Array(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueInt16ArrayContractTest()
    {
        var value = new JavaScriptRuntimeInt16Array(Length: 128, ByteOffset: 8);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromInt16Array(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueInt32ArrayContractTest()
    {
        var value = new JavaScriptRuntimeInt32Array(Length: 64, ByteOffset: 16);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromInt32Array(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueUint32ArrayContractTest()
    {
        var value = new JavaScriptRuntimeUint32Array(Length: 64, ByteOffset: 32);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromUint32Array(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueFloat32ArrayContractTest()
    {
        var value = new JavaScriptRuntimeFloat32Array(Length: 64, ByteOffset: 16);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromFloat32Array(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueFloat64ArrayContractTest()
    {
        var value = new JavaScriptRuntimeFloat64Array(Length: 32, ByteOffset: 32);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromFloat64Array(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueBigInt64ArrayContractTest()
    {
        var value = new JavaScriptRuntimeBigInt64Array(Length: 16, ByteOffset: 64);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromBigInt64Array(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueBigUint64ArrayContractTest()
    {
        var value = new JavaScriptRuntimeBigUint64Array(Length: 16, ByteOffset: 128);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromBigUint64Array(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueAtomicsContractTest()
    {
        var value = new JavaScriptRuntimeAtomics(SupportsWaitAsync: true);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromAtomics(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueProxyContractTest()
    {
        var value = new JavaScriptRuntimeProxy(Revocable: true);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromProxy(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueReflectContractTest()
    {
        var value = new JavaScriptRuntimeReflect(SupportsConstruct: true);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromReflect(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueMathContractTest()
    {
        var value = new JavaScriptRuntimeMath(SupportsRandom: true);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromMath(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueJsonContractTest()
    {
        var value = new JavaScriptRuntimeJson(SupportsRawJson: false);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromJson(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueObjectContractTest()
    {
        var properties = new Dictionary<string, JavaScriptRuntimeValue>(StringComparer.Ordinal)
        {
            ["ok"] = JavaScriptRuntimeValue.FromBoolean(true),
            ["name"] = JavaScriptRuntimeValue.FromString("atom"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

        var value = new JavaScriptRuntimeObject(properties);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromObject(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueFunctionContractTest()
    {
        var function = new JavaScriptRuntimeFunction("connect", Arity: 1);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromFunction(function));

        Assert.That(projected, Is.EqualTo(function));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaquePromiseContractTest()
    {
        var promise = new JavaScriptRuntimePromise(IsSettled: false);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromPromise(promise));

        Assert.That(projected, Is.EqualTo(promise));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueSetContractTest()
    {
        var set = new JavaScriptRuntimeSet(ImmutableArray.Create(
            JavaScriptRuntimeValue.FromBoolean(true),
            JavaScriptRuntimeValue.FromString("atom")));

        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromSet(set));

        Assert.That(projected, Is.EqualTo(set));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueMapContractTest()
    {
        var map = new JavaScriptRuntimeMap(ImmutableArray.Create(
            new KeyValuePair<JavaScriptRuntimeValue, JavaScriptRuntimeValue>(
                JavaScriptRuntimeValue.FromString("name"),
                JavaScriptRuntimeValue.FromString("atom")),
            new KeyValuePair<JavaScriptRuntimeValue, JavaScriptRuntimeValue>(
                JavaScriptRuntimeValue.FromBoolean(true),
                JavaScriptRuntimeValue.FromNumber(1d))));

        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromMap(map));

        Assert.That(projected, Is.EqualTo(map));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueWeakMapContractTest()
    {
        var value = new JavaScriptRuntimeWeakMap(ImmutableArray.Create(
            new KeyValuePair<JavaScriptRuntimeValue, JavaScriptRuntimeValue>(
                JavaScriptRuntimeValue.FromObject(new JavaScriptRuntimeObject(
                    new Dictionary<string, JavaScriptRuntimeValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JavaScriptRuntimeValue.FromNumber(1d),
                    }.ToFrozenDictionary(StringComparer.Ordinal))),
                JavaScriptRuntimeValue.FromString("atom"))));

        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromWeakMap(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueWeakSetContractTest()
    {
        var value = new JavaScriptRuntimeWeakSet(ImmutableArray.Create(
            JavaScriptRuntimeValue.FromObject(new JavaScriptRuntimeObject(
                new Dictionary<string, JavaScriptRuntimeValue>(StringComparer.Ordinal)
                {
                    ["name"] = JavaScriptRuntimeValue.FromString("atom"),
                }.ToFrozenDictionary(StringComparer.Ordinal)))));

        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromWeakSet(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueWeakRefContractTest()
    {
        var value = new JavaScriptRuntimeWeakRef(JavaScriptRuntimeValue.FromObject(new JavaScriptRuntimeObject(
            new Dictionary<string, JavaScriptRuntimeValue>(StringComparer.Ordinal)
            {
                ["kind"] = JavaScriptRuntimeValue.FromString("node"),
            }.ToFrozenDictionary(StringComparer.Ordinal))));

        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromWeakRef(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueFinalizationRegistryContractTest()
    {
        var value = new JavaScriptRuntimeFinalizationRegistry(new JavaScriptRuntimeFunction("cleanup", Arity: 1));
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromFinalizationRegistry(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueRegExpContractTest()
    {
        var value = new JavaScriptRuntimeRegExp("^atom$", "gi");
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromRegExp(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueDateContractTest()
    {
        var value = new JavaScriptRuntimeDate(1_743_000_000_000L);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromDate(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueTypedArrayContractTest()
    {
        var value = new JavaScriptRuntimeTypedArray(JavaScriptRuntimeTypedArrayKind.Float32Array, Length: 24, ByteOffset: 16);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromTypedArray(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueInt8ArrayContractTest()
    {
        var value = new JavaScriptRuntimeInt8Array(Length: 64, ByteOffset: 4);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromInt8Array(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueErrorContractTest()
    {
        var value = new JavaScriptRuntimeError("TypeError", "Invalid receiver.");
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueTypeErrorContractTest()
    {
        var value = new JavaScriptRuntimeTypeError("Invalid receiver.");
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromTypeError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueRangeErrorContractTest()
    {
        var value = new JavaScriptRuntimeRangeError("Argument out of range.");
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromRangeError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueReferenceErrorContractTest()
    {
        var value = new JavaScriptRuntimeReferenceError("Variable is not defined.");
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromReferenceError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueSyntaxErrorContractTest()
    {
        var value = new JavaScriptRuntimeSyntaxError("Unexpected token.");
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromSyntaxError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueUriErrorContractTest()
    {
        var value = new JavaScriptRuntimeUriError("Malformed URI sequence.");
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromUriError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueEvalErrorContractTest()
    {
        var value = new JavaScriptRuntimeEvalError("Invalid eval operation.");
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromEvalError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueAggregateErrorContractTest()
    {
        var value = new JavaScriptRuntimeAggregateError("Multiple failures.", InnerErrorCount: 2);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromAggregateError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueSuppressedErrorContractTest()
    {
        var value = new JavaScriptRuntimeSuppressedError("Suppressed failure.", HasSuppressed: true);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromSuppressedError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueInternalErrorContractTest()
    {
        var value = new JavaScriptRuntimeInternalError("Engine recursion limit exceeded.");
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromInternalError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionRejectsInternalErrorForEcmaScriptSpecificationTest()
    {
        var value = new JavaScriptRuntimeInternalError("Engine recursion limit exceeded.");

        Assert.That(
            () => JavaScriptRuntimeValueProjection.Project(
                JavaScriptRuntimeValue.FromInternalError(value),
                JavaScriptRuntimeSpecification.ECMAScript),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo("JavaScript runtime value kind 'InternalError' is not available when JavaScriptRuntimeSpecification.ECMAScript is selected."));
    }

    [TestCase(nameof(JavaScriptRuntimeValueKind.StackOverflowError))]
    [TestCase(nameof(JavaScriptRuntimeValueKind.TimeoutError))]
    [TestCase(nameof(JavaScriptRuntimeValueKind.MemoryLimitError))]
    [TestCase(nameof(JavaScriptRuntimeValueKind.CancellationError))]
    [TestCase(nameof(JavaScriptRuntimeValueKind.HostInteropError))]
    [TestCase(nameof(JavaScriptRuntimeValueKind.ResourceExhaustedError))]
    public void RuntimeValueProjectionRejectsExtendedOnlyKindsForEcmaScriptSpecificationTest(string valueKindName)
    {
        var valueKind = Enum.Parse<JavaScriptRuntimeValueKind>(valueKindName);

        Assert.That(
            () => JavaScriptRuntimeValueProjection.Project(
                CreateExtendedOnlyValue(valueKind),
                JavaScriptRuntimeSpecification.ECMAScript),
            Throws.TypeOf<NotSupportedException>()
                .With.Message.EqualTo($"JavaScript runtime value kind '{valueKind}' is not available when JavaScriptRuntimeSpecification.ECMAScript is selected."));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueStackOverflowErrorContractTest()
    {
        var value = new JavaScriptRuntimeStackOverflowError("Maximum call stack size exceeded.", RecursionDepth: 1024);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromStackOverflowError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueTimeoutErrorContractTest()
    {
        var value = new JavaScriptRuntimeTimeoutError("Execution timed out.", ElapsedMilliseconds: 5000);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromTimeoutError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueMemoryLimitErrorContractTest()
    {
        var value = new JavaScriptRuntimeMemoryLimitError("Memory budget exceeded.", RequestedBytes: 10485760L);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromMemoryLimitError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueCancellationErrorContractTest()
    {
        var value = new JavaScriptRuntimeCancellationError("Execution canceled.", IsUserInitiated: true);
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromCancellationError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueHostInteropErrorContractTest()
    {
        var value = new JavaScriptRuntimeHostInteropError("Failed to invoke host member.", MemberName: "connect");
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromHostInteropError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    [Test]
    public void RuntimeValueProjectionReturnsOpaqueResourceExhaustedErrorContractTest()
    {
        var value = new JavaScriptRuntimeResourceExhaustedError("Execution resource exhausted.", ResourceName: "worker-budget");
        var projected = JavaScriptRuntimeValueProjection.Project(JavaScriptRuntimeValue.FromResourceExhaustedError(value));

        Assert.That(projected, Is.EqualTo(value));
    }

    private static JavaScriptRuntimeValue CreateExtendedOnlyValue(JavaScriptRuntimeValueKind valueKind)
        => valueKind switch
        {
            JavaScriptRuntimeValueKind.StackOverflowError => JavaScriptRuntimeValue.FromStackOverflowError(new JavaScriptRuntimeStackOverflowError("Maximum call stack size exceeded.", 1024)),
            JavaScriptRuntimeValueKind.TimeoutError => JavaScriptRuntimeValue.FromTimeoutError(new JavaScriptRuntimeTimeoutError("Execution timed out.", 5000)),
            JavaScriptRuntimeValueKind.MemoryLimitError => JavaScriptRuntimeValue.FromMemoryLimitError(new JavaScriptRuntimeMemoryLimitError("Memory budget exceeded.", 1024L)),
            JavaScriptRuntimeValueKind.CancellationError => JavaScriptRuntimeValue.FromCancellationError(new JavaScriptRuntimeCancellationError("Execution canceled.", true)),
            JavaScriptRuntimeValueKind.HostInteropError => JavaScriptRuntimeValue.FromHostInteropError(new JavaScriptRuntimeHostInteropError("Host interop failed.", "connect")),
            JavaScriptRuntimeValueKind.ResourceExhaustedError => JavaScriptRuntimeValue.FromResourceExhaustedError(new JavaScriptRuntimeResourceExhaustedError("Execution resource exhausted.", "worker-budget")),
            _ => throw new ArgumentOutOfRangeException(nameof(valueKind)),
        };
}