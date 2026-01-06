namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Represents the result of an operation that can succeed with a value or fail with an error.
    /// Use this for operations that may fail in expected ways (validation, I/O, bounds checks, etc.)
    /// rather than throwing exceptions.
    /// </summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <typeparam name="TError">The type of the error value.</typeparam>
    public readonly record struct Result<T, TError>
    {
        private readonly T? _value;
        private readonly TError? _error;
        private readonly bool _isSuccess;

        /// <summary>
        /// Creates a successful result with the specified value.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="_">dummy parameter only needed to resolve call to correct constructor overload</param>
        private Result(T value, bool _)
        {
            _value = value;
            _error = default;
            _isSuccess = true;
        }

        /// <summary>
        /// Creates an error result with the specified error.
        /// </summary>
        /// <param name="error"></param>
        private Result(TError error)
        {
            _value = default;
            _error = error;
            _isSuccess = false;
        }

        /// <summary>
        /// True if the operation succeeded.
        /// </summary>
        public bool IsSuccess => _isSuccess;

        /// <summary>
        /// True if the operation failed.
        /// </summary>
        public bool IsFailure => !_isSuccess;

        /// <summary>
        /// The success value. Throws if IsFailure.
        /// </summary>
        public T Value => _isSuccess
            ? _value!
            : throw new InvalidOperationException($"Cannot access Value on failed Result: {_error}");

        /// <summary>
        /// The error value. Throws if IsSuccess.
        /// </summary>
        public TError Error => !_isSuccess
            ? _error!
            : throw new InvalidOperationException("Cannot access Error on successful Result");

        /// <summary>
        /// The error value, or default if IsSuccess.
        /// </summary>
        public TError? ErrorOrDefault => _error;

        /// <summary>
        /// Creates a successful result with the specified value.
        /// </summary>
        public static Result<T, TError> Ok(T value) => new(value, true);

        /// <summary>
        /// Creates a failed result with the specified error.
        /// To create an error result, use Result&lt;TError&gt;.Err(error) and 
        /// cast the Result&lt;TError&gt; to Result&lt;T, TError&gt;
        /// </summary>
        internal static Result<T, TError> ValueResultErr(TError error) => new(error);

        /// <summary>
        /// Implicitly converts a void-style Result (Result&lt;TError&gt;) into a value Result (Result&lt;T, TError&gt;).
        /// On success the conversion returns a null/default error. On failure the error is preserved.
        /// In other words, this should only be used to cast the return value of Result<typeparamref name="TError"/>.Err(error).
        /// </summary>
        /// <param name="result">Source Result&lt;TError&gt; to convert.</param>
        /// <returns>Converted Result&lt;T, TError&gt;.</returns>
        public static implicit operator Result<T, TError>(Result<TError> result) => 
            result.IsSuccess ? Result<T, TError>.Ok(default) : Result<T, TError>.ValueResultErr(result.Error);

        /// <summary>
        /// Deconstructs the result for pattern matching.
        /// The Deconstruct method allows usage like:
        ///    var (isSuccess, value, error) = result;
        /// </summary>
        public void Deconstruct(out bool isSuccess, out T? value, out TError? error)
        {
            isSuccess = _isSuccess;
            value = _value;
            error = _error;
        }

        /// <summary>
        /// Try to get the value. Returns false if failed.
        /// </summary>
        public bool TryGetValue(out T? value)
        {
            value = _value;
            return _isSuccess;
        }

        /// <summary>
        /// Try to get the error. Returns false if succeeded.
        /// </summary>
        /// <returns>True if the result is a failure and the error is set; otherwise, false.</returns>
        public bool TryGetError(out TError? error)
        {
            error = _error;
            return !_isSuccess;
        }

        /// <summary>
        /// Returns the value if successful, or the specified default if failed.
        /// </summary>
        public T ValueOr(T defaultValue) => _isSuccess ? _value! : defaultValue;

        /// <summary>
        /// Returns the value if successful, or invokes the factory to get a default.
        /// </summary>
        public T ValueOr(Func<TError, T> defaultFactory) =>
            _isSuccess ? _value! : defaultFactory(_error!);

        /// <summary>
        /// Chains a transform that cannot fail.
        /// If successful, applies the transform to the value; otherwise propagates the error.
        /// </summary>
        /// <example>
        /// result.Then(x => x.ToString())  // Transform int to string
        /// </example>
        public Result<TNew, TError> Then<TNew>(Func<T, TNew> transform) =>
            _isSuccess ? Result<TNew, TError>.Ok(transform(_value!)) : Result<TNew, TError>.ValueResultErr(_error!);

        /// <summary>
        /// Chains an operation that can fail.
        /// If successful, executes the next operation; otherwise propagates the error.
        /// </summary>
        /// <example>
        /// result.Then(x => Validate(x))  // Validate returns Result
        /// </example>
        public Result<TNew, TError> Then<TNew>(Func<T, Result<TNew, TError>> nextStep) =>
            _isSuccess ? nextStep(_value!) : Result<TNew, TError>.ValueResultErr(_error!);

        /// <summary>
        /// Transforms the error if failed, preserving success. This is useful when
        ///  - Converting between error types at layer boundaries
        ///  - Adding context to errors as they bubble up
        ///  - Translating technical errors to user-friendly messages
        /// </summary>
        public Result<T, TNewError> MapError<TNewError>(Func<TError, TNewError> transform) =>
            _isSuccess ? Result<T, TNewError>.Ok(_value!) : Result<T, TNewError>.ValueResultErr(transform(_error!));

        /// <summary>
        /// Executes an action if successful, returns self for chaining.
        /// </summary>
        public Result<T, TError> OnSuccess(Action<T> action)
        {
            if (_isSuccess) action(_value!);
            return this;
        }

        /// <summary>
        /// Executes an action if failed, returns self for chaining.
        /// </summary>
        public Result<T, TError> OnFailure(Action<TError> action)
        {
            if (!_isSuccess) action(_error!);
            return this;
        }

        /// <summary>
        /// Pattern matches on success or failure.
        /// </summary>
        public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<TError, TResult> onFailure) =>
            _isSuccess ? onSuccess(_value!) : onFailure(_error!);

        /// <summary>
        /// Converts to a nullable, returning null on failure.
        /// </summary>
        public T? ToNullable() => _isSuccess ? _value : default;

        /// <summary>
        /// Unwraps the value, throwing if failed. Alias for Value property.
        /// </summary>
        public T Unwrap() => Value;

        /// <summary>
        /// Unwraps the error, throwing if successful. Alias for Error property.
        /// </summary>
        public TError UnwrapError() => Error;
    }

    /// <summary>
    /// Represents the result of an operation that can succeed (no value) or fail with an error.
    /// Use for void-returning operations that may fail.
    /// </summary>
    /// <typeparam name="TError">The type of the error value.</typeparam>
    public readonly record struct Result<TError>
    {
        private readonly TError? _error;
        private readonly bool _isSuccess;

        private Result(bool isSuccess, TError? error = default)
        {
            _isSuccess = isSuccess;
            _error = error;
        }

        /// <summary>
        /// True if the operation succeeded.
        /// </summary>
        public bool IsSuccess => _isSuccess;

        /// <summary>
        /// True if the operation failed.
        /// </summary>
        public bool IsFailure => !_isSuccess;

        /// <summary>
        /// The error value. Throws if IsSuccess.
        /// </summary>
        public TError Error => !_isSuccess
            ? _error!
            : throw new InvalidOperationException("Cannot access Error on successful Result");

        /// <summary>
        /// The error value, or default if IsSuccess.
        /// </summary>
        public TError? ErrorOrDefault => _error;

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static Result<TError> Ok() => new(true);

        /// <summary>
        /// Creates a failed result with the specified error.
        /// </summary>
        public static Result<TError> Err(TError? error = default) => new(false, error);

        /// <summary>
        /// Deconstructs the result for pattern matching.
        /// Allows for usage like:
        ///   var (isSuccess, error) = result;
        /// </summary>
        public void Deconstruct(out bool isSuccess, out TError? error)
        {
            isSuccess = _isSuccess;
            error = _error;
        }

        /// <summary>
        /// Try to get the error.
        /// </summary>
        /// <returns>True if the result is a failure and the error is set; otherwise, false.</returns>
        public bool TryGetError(out TError? error)
        {
            error = _error;
            return !_isSuccess;
        }

        /// <summary>
        /// Chains an operation that can fail.
        /// If successful, executes the next operation; otherwise propagates the error.
        /// </summary>
        public Result<TError> Then(Func<Result<TError>> nextStep) =>
            _isSuccess ? nextStep() : this;

        /// <summary>
        /// Transforms the error if failed, preserving success. This is useful when
        ///  - Converting between error types at layer boundaries
        ///  - Adding context to errors as they bubble up
        ///  - Translating technical errors to user-friendly messages
        /// </summary>
        public Result<TNewError> MapError<TNewError>(Func<TError, TNewError> transform) =>
            _isSuccess ? Result<TNewError>.Ok() : Result<TNewError>.Err(transform(_error!));

        /// <summary>
        /// Executes an action if successful, returns self for chaining.
        /// </summary>
        public Result<TError> OnSuccess(Action action)
        {
            if (_isSuccess) action();
            return this;
        }

        /// <summary>
        /// Executes an action if failed, returns self for chaining.
        /// </summary>
        public Result<TError> OnFailure(Action<TError> action)
        {
            if (!_isSuccess) action(_error!);
            return this;
        }

        /// <summary>
        /// Pattern matches on success or failure.
        /// </summary>
        public TResult Match<TResult>(Func<TResult> onSuccess, Func<TError, TResult> onFailure) =>
            _isSuccess ? onSuccess() : onFailure(_error!);

        /// <summary>
        /// Converts to Result&lt;T, TError&gt; with the specified value on success.
        /// </summary>
        public Result<T, TError> WithValue<T>(T value) =>
            _isSuccess ? Result<T, TError>.Ok(value) : Err(_error!);

        /// <summary>
        /// Combines multiple Results, returning first failure or success if all succeed.
        /// </summary>
        public static Result<TError> Combine(params Result<TError>[] results)
        {
            foreach (var result in results)
            {
                if (result.IsFailure) return result;
            }
            return Ok();
        }
    }

    /// <summary>
    /// Extension methods for async Result operations.
    /// </summary>
    public static class ResultExtensions
    {
        /// <summary>
        /// Converts a tuple of (error, value) to Result&lt;T, TError&gt;.
        /// </summary>
        public static Result<T, TError> ToResult<T, TError>(this (TError? error, T? value) tuple)
            where TError : class =>
            tuple.error == null ? Result<T, TError>.Ok(tuple.value!) : Result<T, TError>.ValueResultErr(tuple.error);

        /// <summary>
        /// Converts a nullable error to Result&lt;TError&gt; (null = success).
        /// </summary>
        public static Result<TError> ToResult<TError>(this TError? error) where TError : class =>
            error == null ? Result<TError>.Ok() : Result<TError>.Err(error);

        /// <summary>
        /// Chains an async transform that cannot fail.
        /// </summary>
        /// <example>
        /// await GetDataAsync().Then(x => x.ToString())
        /// </example>
        public static async Task<Result<TNew, TError>> Then<T, TNew, TError>(
            this Task<Result<T, TError>> resultTask,
            Func<T, TNew> transform)
        {
            var result = await resultTask;
            return result.Then(transform);
        }

        /// <summary>
        /// Chains a sync operation that can fail after an async Result.
        /// </summary>
        /// <example>
        /// await GetDataAsync().Then(x => Validate(x))
        /// </example>
        public static async Task<Result<TNew, TError>> Then<T, TNew, TError>(
            this Task<Result<T, TError>> resultTask,
            Func<T, Result<TNew, TError>> nextStep)
        {
            var result = await resultTask;
            return result.Then(nextStep);
        }

        /// <summary>
        /// Chains an async operation that can fail.
        /// </summary>
        /// <example>
        /// await GetDataAsync()
        ///     .Then(data => ProcessAsync(data))
        ///     .Then(processed => SaveAsync(processed));
        /// </example>
        public static async Task<Result<TNew, TError>> Then<T, TNew, TError>(
            this Task<Result<T, TError>> resultTask,
            Func<T, Task<Result<TNew, TError>>> nextStep)
        {
            var result = await resultTask;
            return result.IsSuccess
                ? await nextStep(result.Value)
                : Result<TNew, TError>.ValueResultErr(result.Error);
        }

        /// <summary>
        /// Chains async operations for void Results.
        /// </summary>
        public static async Task<Result<TError>> Then<TError>(
            this Task<Result<TError>> resultTask,
            Func<Task<Result<TError>>> nextStep)
        {
            var result = await resultTask;
            return result.IsSuccess ? await nextStep() : result;
        }

        /// <summary>
        /// Chains a sync operation for void Results after an async Result.
        /// </summary>
        public static async Task<Result<TError>> Then<TError>(
            this Task<Result<TError>> resultTask,
            Func<Result<TError>> nextStep)
        {
            var result = await resultTask;
            return result.Then(nextStep);
        }

        /// <summary>
        /// Transforms the error of an async Result if failed.
        /// </summary>
        public static async Task<Result<T, TNewError>> ThenError<T, TError, TNewError>(
            this Task<Result<T, TError>> resultTask,
            Func<TError, TNewError> transform)
        {
            var result = await resultTask;
            return result.MapError(transform);
        }
    }
}