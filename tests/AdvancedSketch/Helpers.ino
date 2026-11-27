Counter nextCounter(const Counter& input)
{
 Counter out = { static_cast<uint8_t>(input.value + heroAlpha()) };
 return out;
}
