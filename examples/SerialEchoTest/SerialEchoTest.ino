void setup(){Serial.begin(38400);pinMode(A0,OUTPUT);Serial.println(F("HERO SERIAL TEST"));}
void loop(){static unsigned long t=0;if(millis()-t>=1000){t=millis();digitalWrite(A0,!digitalRead(A0));Serial.println(F("HERO heartbeat"));}while(Serial.available())Serial.write(Serial.read());}
