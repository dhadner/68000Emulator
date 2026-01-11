using Xunit;
using PendleCodeMonkey.MC68000EmulatorLib;
using System;
using System.Collections.Generic;

namespace PendleCodeMonkey.MC68000Emulator.Tests
{
    public class ResultTests
    {
        #region Result<byte, string> Tests

        [Fact]
        public void Result_Byte_Ok_CreatesSuccess()
        {
            byte value = 42;
            Result<byte, string> result = Ok(value);

            Assert.True(result.IsSuccess);
            Assert.False(result.IsFailure);
            Assert.Equal(value, result.Value);
            Assert.Throws<InvalidOperationException>(() => result.Error);
        }

        [Fact]
        public void Result_Byte_Err_CreatesFailure()
        {
            string error = "Something went wrong";
            Result<byte, string> result = Err(error);

            Assert.True(result.IsFailure);
            Assert.False(result.IsSuccess);
            Assert.Equal(error, result.Error);
            Assert.Equal(error, result.ErrorOrDefault);
            Assert.Throws<InvalidOperationException>(() => result.Value);
        }

        [Fact]
        public void Result_Byte_Deconstruct_Success()
        {
            byte initialValue = 100;
            Result<byte, string> result = Ok(initialValue);
            var (isSuccess, value, error) = result;

            Assert.True(isSuccess);
            Assert.Equal(initialValue, value);
            Assert.Null(error);
        }

        [Fact]
        public void Result_Byte_Deconstruct_Failure()
        {
            string errorMsg = "Error";
            Result<byte, string> result = Err(errorMsg);
            var (isSuccess, value, error) = result;

            Assert.False(isSuccess);
            Assert.Equal(default(byte), value); // Value is default on failure
            Assert.Equal(errorMsg, error);
        }

        #endregion

        #region Result<ushort, string> Tests

        [Fact]
        public void Result_UShort_Ok_CreatesSuccess()
        {
            ushort value = 12345;
            Result<ushort, string> result = Ok(value);

            Assert.True(result.IsSuccess);
            Assert.Equal(value, result.Value);
        }

        [Fact]
        public void Result_UShort_Err_CreatesFailure()
        {
            string error = "UShort error";
            Result<ushort, string> result = Err(error);

            Assert.True(result.IsFailure);
            Assert.Equal(error, result.Error);
        }

        #endregion

        #region Result<uint, string> Tests

        [Fact]
        public void Result_UInt_Ok_CreatesSuccess()
        {
            uint value = 123456789;
            Result<uint, string> result = Ok(value);

            Assert.True(result.IsSuccess);
            Assert.Equal(value, result.Value);
        }

        [Fact]
        public void Result_UInt_Err_CreatesFailure()
        {
            string error = "UInt error";
            Result<uint, string> result = Err(error);

            Assert.True(result.IsFailure);
            Assert.Equal(error, result.Error);
        }

        #endregion

        #region Result<byte[], string> Tests

        [Fact]
        public void Result_ByteArray_Ok_CreatesSuccess()
        {
            byte[] value = [1, 2, 3];
            Result<byte[], string> result = Ok(value);

            Assert.True(result.IsSuccess);
            Assert.Equal(value, result.Value);
        }

        [Fact]
        public void Result_ByteArray_Ok_WithNull_CreatesEmptyArray_ViaDefaultMechanism()
        {
            // The Result struct has logic to replace null/default collections with empty ones in constructor.
            // Result<T, TError>.Ok(null) calls new(T? value, bool _) which calls IsNullOrDefault and GetDefaultValue.
            byte[]? values = null;
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
            Result<byte[], string> result = Ok(values);
#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Empty(result.Value); // Should be empty array, not null
        }

        [Fact]
        public void Result_ByteArray_Ok_WithDefault_CreatesEmptyArray()
        {
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
            Result<byte[], string> result = Ok(default(byte[]));
#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Empty(result.Value);
        }

        [Fact]
        public void Result_ByteArray_Err_CreatesFailure()
        {
            string error = "Byte array error";
            Result<byte[], string> result = Err(error);

            Assert.True(result.IsFailure);
            Assert.Equal(error, result.Error);
        }

        #endregion

        #region Result<string> (Void Result) Tests

        // This corresponds to Result<TError> where TError is string.
        
        [Fact]
        public void Result_StringError_Ok_CreatesSuccess()
        {
            var result = Ok();

            Assert.True(result.IsSuccess);
            Assert.False(result.IsFailure);
            Assert.Throws<InvalidOperationException>(() => result.Error);
        }

        [Fact]
        public void Result_StringError_Err_CreatesFailure()
        {
            string error = "Void operation failed";
            var result = Err(error);

            Assert.True(result.IsFailure);
            Assert.False(result.IsSuccess);
            Assert.Equal(error, result.Error);
        }

        [Fact]
        public void Result_StringError_Deconstruct()
        {
            var result = Err("Fail");
            var (isSuccess, error) = result;

            Assert.False(isSuccess);
            Assert.Equal("Fail", error);

            var okResult = Result<string>.Ok();
            var (okSuccess, okError) = okResult;
            Assert.True(okSuccess);
            Assert.Null(okError);
        }

        [Fact]
        public void Result_StringError_ImplicitConversionOfInt_To_ResultType()
        {
            // Implicit operator Result<T, TError>(Result<TError> result)
            var voidResult = Err("Implicit error");
            Result<int, string> valResult = voidResult;

            Assert.True(valResult.IsFailure);
            Assert.Equal("Implicit error", valResult.Error);

            var voidSuccess = Ok();
            Result<int, string> valSuccess;
            Assert.Throws<InvalidOperationException>(() => valSuccess = voidSuccess);
        }

        [Fact]
        public void Result_StringError_ImplicitConversionOfNull_To_ResultType()
        {
            // Implicit operator Result<T, TError>(Result<TError> result)
            var voidResult = Err("Implicit error");
            Result<string, string> valResult = voidResult;

            Assert.True(valResult.IsFailure);
            Assert.Equal("Implicit error", valResult.Error);

            var voidSuccess = Ok();
            Result<string, string> valSuccess = voidSuccess;
            Assert.True(valSuccess.IsSuccess);
            Assert.Equal("", valSuccess.Value);
        }

        [Fact]
        public void Result_StringError_Combine()
        {
            var r1 = Ok();
            var r2 = Ok();
            var combined = Combine(r1, r2);
            Assert.True(combined.IsSuccess);

            var rFail = Err("One failed");
            var combinedFail = Combine(r1, rFail, r2);
            Assert.True(combinedFail.IsFailure);
            Assert.Equal("One failed", combinedFail.Error);
        }

        #endregion

        #region Functional Method Tests (Chain, Map, Match, etc.)

        [Fact]
        public void ValueOr_ReturnsDefaultOnFailure()
        {
            Result<int, string> result = Err("Fail");
            Assert.Equal(99, result.ValueOr(99));

            Result<int, string> resultOk = Ok(10);
            Assert.Equal(10, resultOk.ValueOr(99));
        }

        [Fact]
        public void MapError_TransformsError()
        {
            Result<int, string> result = Err("Fail");
            var transformed = result.MapError(e => e + "!");

            Assert.True(transformed.IsFailure);
            Assert.Equal("Fail!", transformed.Error);
        }

        [Fact]
        public void Then_TransformsValue()
        {
            Result<int, string> result = Ok(10);
            var transformed = result.Then(x => x.ToString());

            Assert.True(transformed.IsSuccess);
            Assert.Equal("10", transformed.Value);
        }

        [Fact]
        public void Then_ChainsResultReturningFunc()
        {
            Result<int, string> result = Ok(10);
            var next = result.Then(x => Result<string, string>.Ok("Success"));

            Assert.True(next.IsSuccess);
            Assert.Equal("Success", next.Value);

            var failNext = result.Then(x => Result<string, string>.Err("Next failed"));
            Assert.True(failNext.IsFailure);
            Assert.Equal("Next failed", failNext.Error);
        }

        [Fact]
        public void OnSuccess_WrapsAction()
        {
            bool called = false;
            Result<int, string>.Ok(1).OnSuccess(x => called = true);
            Assert.True(called);

            called = false;
            Result<int, string>.Err("e").OnSuccess(x => called = true);
            Assert.False(called);
        }

        [Fact]
        public void OnFailure_WrapsAction()
        {
            bool called = false;
            Result<int, string>.Err("e").OnFailure(e => called = true);
            Assert.True(called);

            called = false;
            Result<int, string>.Ok(1).OnFailure(e => called = true);
            Assert.False(called);
        }

        [Fact]
        public void Match_ReturnsResultBasedOnState()
        {
            var resultOk = Result<int, string>.Ok(5);
            var valOk = resultOk.Match(
                onSuccess: x => x * 2,
                onFailure: e => -1
            );
            Assert.Equal(10, valOk);

            var resultFail = Result<int, string>.Err("error");
            var valFail = resultFail.Match(
                onSuccess: x => x * 2,
                onFailure: e => -1
            );
            Assert.Equal(-1, valFail);
        }

        [Fact]
        public void ToNullable_ReturnsValueOrNull()
        {
            var ok = Ok(10);
            Assert.Equal(10, ok.ToNullable());

            var fail = (Result<int, string>)Result<string>.Err("e");
            Assert.Equal(default, fail.ToNullable());
        }

        [Fact]
        public void Unwrap_Methods()
        {
            var ok = Result<int, string>.Ok(10);
            Assert.Equal(10, ok.Unwrap());
            Assert.Throws<InvalidOperationException>(() => ok.UnwrapError());

            var fail = (Result<int, string>)Result<string>.Err("error");
            Assert.Equal("error", fail.UnwrapError());
            Assert.Throws<InvalidOperationException>(() => fail.Unwrap());
        }
        
        [Fact]
        public void WithValue_ConvertsVoidResult()
        {
            Result<string> voidOk = Ok();
            Result<int, string> valOk = voidOk.WithValue(123);
            Assert.True(valOk.IsSuccess);
            Assert.Equal(123, valOk.Value);

            Result<string> voidFail = Result<string>.Err("Fail");
            Result<int, string> valFail = voidFail.WithValue(123);
            Assert.True(valFail.IsFailure);
            Assert.Equal("Fail", valFail.Error);
        }

        #endregion
    }
}
