using System;
using System.Collections.Generic;
using FlightDeck.Core;
using Xunit;

namespace FlightDeck.Core.Tests
{
    public class JsonTests
    {
        [Fact]
        public void SerializesScalars()
        {
            Assert.Equal("null", Json.Serialize(null));
            Assert.Equal("true", Json.Serialize(true));
            Assert.Equal("\"hi\"", Json.Serialize("hi"));
            Assert.Equal("42", Json.Serialize(42));
            Assert.Equal("0.5", Json.Serialize(0.5d));
        }

        [Fact]
        public void SerializesNonFiniteNumbersAsNull()
        {
            Assert.Equal("null", Json.Serialize(double.NaN));
            Assert.Equal("null", Json.Serialize(double.PositiveInfinity));
            Assert.Equal("null", Json.Serialize(float.NegativeInfinity));
        }

        [Fact]
        public void SerializesNestedStructures()
        {
            var payload = new Dictionary<string, object>
            {
                { "ok", true },
                { "items", new List<object> { 1, "two", new Dictionary<string, object> { { "three", 3 } } } }
            };

            Assert.Equal("{\"ok\":true,\"items\":[1,\"two\",{\"three\":3}]}", Json.Serialize(payload));
        }

        [Fact]
        public void EscapesControlCharacters()
        {
            Assert.Equal("\"a\\\"b\\\\c\\n\\t\\u0001\"", Json.Serialize("a\"b\\c\n\t\u0001"));
        }

        [Fact]
        public void ParsesObjectsAndArrays()
        {
            var parsed = Json.AsObject(Json.Parse("{\"a\":[1,2.5,\"x\",true,null],\"b\":{\"c\":-3}}"));

            Assert.NotNull(parsed);
            var list = Assert.IsType<List<object>>(parsed["a"]);
            Assert.Equal(5, list.Count);
            Assert.Equal(1L, list[0]);
            Assert.Equal(2.5d, list[1]);
            Assert.Equal("x", list[2]);
            Assert.True((bool)list[3]);
            Assert.Null(list[4]);

            var nested = Json.AsObject(parsed["b"]);
            Assert.Equal(-3L, nested["c"]);
        }

        [Fact]
        public void ParsesUnicodeEscapes()
        {
            Assert.Equal("\u00e9", Json.Parse("\"\\u00e9\""));
        }

        [Theory]
        [InlineData("{")]
        [InlineData("{\"a\":}")]
        [InlineData("[1,]")]
        [InlineData("\"unterminated")]
        [InlineData("{} trailing")]
        [InlineData("01x")]
        public void RejectsMalformedInput(string text)
        {
            Assert.Throws<JsonException>(delegate { Json.Parse(text); });
        }

        [Fact]
        public void ReadsCaseInsensitivelyAndCoerces()
        {
            var payload = Json.AsObject(Json.Parse("{\"Throttle\":\"0.25\",\"Enabled\":1,\"Name\":\"x\"}"));

            Assert.Equal(0.25d, Json.GetDouble(payload, "throttle"));
            Assert.True(Json.GetBool(payload, "enabled"));
            Assert.Equal("x", Json.GetString(payload, "name"));
            Assert.Null(Json.GetDouble(payload, "missing"));
        }

        [Fact]
        public void ReadsFirstPresentAlias()
        {
            var payload = Json.AsObject(Json.Parse("{\"value\":3}"));

            Assert.Equal(3d, Json.GetDouble(payload, "Throttle", "value"));
        }
    }
}
