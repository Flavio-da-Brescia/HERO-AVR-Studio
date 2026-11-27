void setup(){Serial.begin(38400);pinMode(A0,OUTPUT);}
void loop(){static unsigned int n=0;Serial.print(F("step:"));Serial.print(n);Serial.print(F(" wave:"));Serial.println((int)(n%20)-10);digitalWrite(A0,n%2);n++;delay(250);}
