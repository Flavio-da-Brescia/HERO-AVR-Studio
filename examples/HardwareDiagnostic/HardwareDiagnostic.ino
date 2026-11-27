#include <Arduino.h>
#include <string.h>
char line[96];
uint8_t used=0;
bool overflow=false;
void setup(){
  Serial.begin(38400);
  Serial.println(F("HERO HardwareDiagnostic | ATmega328P 3.3V 8MHz"));
}
void respond(){
  char *token=strtok(line," ");
  char *cmd=strtok(NULL," ");
  if(!token || !cmd || strlen(token)!=12 || strtok(NULL," "))return;
  if(strcmp(cmd,"IDENT") && strcmp(cmd,"ECHO") && strcmp(cmd,"ADC3") && strcmp(cmd,"UPTIME"))return;
  Serial.print(F("HERO "));Serial.print(token);Serial.print(' ');Serial.print(cmd);Serial.print(' ');
  if(!strcmp(cmd,"IDENT"))Serial.println(F("ATmega328P-8MHz"));
  else if(!strcmp(cmd,"ECHO"))Serial.println(F("OK"));
  else if(!strcmp(cmd,"ADC3"))Serial.println(analogRead(A3));
  else if(!strcmp(cmd,"UPTIME"))Serial.println(millis());
}
void loop(){
  while(Serial.available()){
    char c=Serial.read();
    if(c=='\r')continue;
    if(c=='\n'){if(!overflow){line[used]=0;respond();}used=0;overflow=false;}
    else if(!overflow){if(used<sizeof(line)-1)line[used++]=c;else overflow=true;}
  }
}
