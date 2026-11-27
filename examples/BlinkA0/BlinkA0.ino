void setup() {
  pinMode(A0, OUTPUT);
  Serial.begin(38400);
  Serial.println(F("HERO AVR Studio | ATmega328P 3.3V 8MHz | Blink A0"));
}
void loop() {
  digitalWrite(A0, HIGH);
  delay(500);
  digitalWrite(A0, LOW);
  delay(500);
}
