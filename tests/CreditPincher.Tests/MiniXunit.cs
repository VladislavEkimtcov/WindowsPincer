using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

// A stand-in for the slice of xUnit these tests use.
//
// xUnit arrives through NuGet, and a bare Windows install has no NuGet, no .NET SDK
// and no `dotnet test`. Rather than drop the test suite, the attributes and asserts
// it depends on are reimplemented here — about 150 lines — so the tests below are
// still the original tests, compiled by the C# compiler that ships with Windows and
// run as a plain console executable.
namespace Xunit
{
    /// <summary>Marks a parameterless test method.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class FactAttribute : Attribute
    {
    }

    /// <summary>Marks a test method driven by one or more <see cref="InlineDataAttribute"/>.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TheoryAttribute : Attribute
    {
    }

    /// <summary>One argument set for a <see cref="TheoryAttribute"/> method.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class InlineDataAttribute : Attribute
    {
        public InlineDataAttribute(params object[] values)
        {
            Values = values;
        }

        public object[] Values { get; private set; }
    }

    /// <summary>Raised by a failing assertion.</summary>
    public sealed class AssertionException : Exception
    {
        public AssertionException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// The assertions used by this suite. Values arrive boxed, which is deliberate:
    /// boxing a <c>Nullable&lt;T&gt;</c> yields either null or a boxed T, so the same
    /// overload handles <c>DateOnly</c> and <c>DateOnly?</c> without any generic
    /// inference puzzles.
    /// </summary>
    public static class Assert
    {
        public static void Equal(object expected, object actual)
        {
            if (!object.Equals(expected, actual))
            {
                throw new AssertionException(
                    "Expected " + Describe(expected) + " but got " + Describe(actual) + ".");
            }
        }

        /// <summary>Compares to <paramref name="precision"/> decimal places, as xUnit does.</summary>
        public static void Equal(double expected, double actual, int precision)
        {
            if (Math.Round(expected, precision) != Math.Round(actual, precision))
            {
                throw new AssertionException(
                    "Expected " + expected.ToString("R", CultureInfo.InvariantCulture) +
                    " but got " + actual.ToString("R", CultureInfo.InvariantCulture) +
                    " (to " + precision + " places).");
            }
        }

        public static void True(bool condition)
        {
            if (!condition)
            {
                throw new AssertionException("Expected true but got false.");
            }
        }

        public static void False(bool condition)
        {
            if (condition)
            {
                throw new AssertionException("Expected false but got true.");
            }
        }

        public static void Null(object value)
        {
            if (value != null)
            {
                throw new AssertionException("Expected null but got " + Describe(value) + ".");
            }
        }

        public static void NotNull(object value)
        {
            if (value == null)
            {
                throw new AssertionException("Expected a value but got null.");
            }
        }

        public static void StartsWith(string expected, string actual)
        {
            if (actual == null || !actual.StartsWith(expected, StringComparison.Ordinal))
            {
                throw new AssertionException(
                    "Expected " + Describe(actual) + " to start with " + Describe(expected) + ".");
            }
        }

        public static void EndsWith(string expected, string actual)
        {
            if (actual == null || !actual.EndsWith(expected, StringComparison.Ordinal))
            {
                throw new AssertionException(
                    "Expected " + Describe(actual) + " to end with " + Describe(expected) + ".");
            }
        }

        public static void Contains(string expected, string actual)
        {
            if (actual == null || actual.IndexOf(expected, StringComparison.Ordinal) < 0)
            {
                throw new AssertionException(
                    "Expected " + Describe(actual) + " to contain " + Describe(expected) + ".");
            }
        }

        public static void Single(IEnumerable sequence)
        {
            var count = sequence.Cast<object>().Count();
            if (count != 1)
            {
                throw new AssertionException("Expected exactly one item but found " + count + ".");
            }
        }

        public static T Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (T expected)
            {
                return expected;
            }
            catch (Exception unexpected)
            {
                throw new AssertionException(
                    "Expected " + typeof(T).Name + " but got " + unexpected.GetType().Name + ".");
            }

            throw new AssertionException("Expected " + typeof(T).Name + " but nothing was thrown.");
        }

        private static string Describe(object value)
        {
            if (value == null)
            {
                return "null";
            }

            var text = value as string;
            if (text != null)
            {
                return "\"" + text + "\"";
            }

            var formattable = value as IFormattable;
            return formattable != null
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();
        }
    }
}

namespace CreditPincher.Tests
{
    /// <summary>
    /// Discovers and runs every <c>[Fact]</c> and <c>[Theory]</c> in this assembly.
    /// One fresh instance per test, and <see cref="IDisposable"/> is honoured, so the
    /// tests behave the way they did under xUnit.
    /// </summary>
    public static class TestRunner
    {
        public static int Main()
        {
            var passed = 0;
            var failures = new List<string>();

            var testClasses = typeof(TestRunner).Assembly
                .GetTypes()
                .Where(type => type.IsClass && !type.IsAbstract)
                .OrderBy(type => type.Name);

            foreach (var testClass in testClasses)
            {
                foreach (var method in testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                             .OrderBy(method => method.Name))
                {
                    foreach (var arguments in ArgumentSetsFor(method))
                    {
                        var name = testClass.Name + "." + method.Name + Describe(arguments);

                        try
                        {
                            Invoke(testClass, method, arguments);
                            passed++;
                        }
                        catch (Exception exception)
                        {
                            failures.Add(name + Environment.NewLine + "    " + Unwrap(exception));
                        }
                    }
                }
            }

            Console.WriteLine();
            foreach (var failure in failures)
            {
                Console.WriteLine("FAILED  " + failure);
            }

            Console.WriteLine(
                failures.Count == 0
                    ? "All " + passed + " tests passed."
                    : passed + " passed, " + failures.Count + " FAILED.");

            return failures.Count == 0 ? 0 : 1;
        }

        private static IEnumerable<object[]> ArgumentSetsFor(MethodInfo method)
        {
            if (method.GetCustomAttributes(typeof(Xunit.FactAttribute), false).Length > 0)
            {
                return new[] { new object[0] };
            }

            if (method.GetCustomAttributes(typeof(Xunit.TheoryAttribute), false).Length > 0)
            {
                return method
                    .GetCustomAttributes(typeof(Xunit.InlineDataAttribute), false)
                    .Cast<Xunit.InlineDataAttribute>()
                    .Select(data => data.Values);
            }

            return Enumerable.Empty<object[]>();
        }

        private static void Invoke(Type testClass, MethodInfo method, object[] arguments)
        {
            var instance = Activator.CreateInstance(testClass);

            try
            {
                method.Invoke(instance, arguments);
            }
            finally
            {
                var disposable = instance as IDisposable;
                if (disposable != null)
                {
                    disposable.Dispose();
                }
            }
        }

        private static string Describe(object[] arguments)
        {
            if (arguments.Length == 0)
            {
                return string.Empty;
            }

            return "(" + string.Join(", ", arguments.Select(argument =>
                Convert.ToString(argument, CultureInfo.InvariantCulture))) + ")";
        }

        private static string Unwrap(Exception exception)
        {
            var invocation = exception as TargetInvocationException;
            var actual = invocation != null && invocation.InnerException != null
                ? invocation.InnerException
                : exception;

            return actual is Xunit.AssertionException
                ? actual.Message
                : actual.GetType().Name + ": " + actual.Message;
        }
    }
}
