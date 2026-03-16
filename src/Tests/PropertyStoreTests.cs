using Xunit;

namespace Captura.Tests
{
    [Collection(nameof(Tests))]
    public class PropertyStoreTests
    {
        [Fact]
        public void TestPropertyChanged()
        {
            var obj = new FakePropertyStore();

            var propertyName = "Unknown";

            obj.PropertyChanged += (S, E) => propertyName = E.PropertyName;

            const string newValue = "New Value";

            obj.Property = newValue;

            Assert.Equal(newValue, obj.Property);
            Assert.Equal(nameof(obj.Property), propertyName);
        }

        [Fact]
        public void CheckDefaultValue()
        {
            var obj = new FakePropertyStore();
            
            Assert.Equal(FakePropertyStore.DefaultPropertyValue, obj.Property);
        }
    }
}