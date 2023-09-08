# 🏷️Chat Application

## 📌protocol 설계

### INIT Type

- INIT:fromID: // 채팅 시작

### SEND Type

- SEND:BR:fromID:MSG: // 브로드 캐스트
- SEND:UNI:fromID:toID:MSG: // 유니 캐스트
- SEND:MUL:fromID:toID리스트:MSG: // 멀티 캐스트

### INFO Type

- INFO:WHO: // 접속중인 사용자 출력
- INFO:WC: // 접속중인 사용자수 출력

### SET Type

- SET:MUTE:fromID:toID: // 특정 사용자 차단

### Response

- ID_Changed:fromID // ID가 이미 존재하는 경우 난수를 이용해 문제 해결

- BR_Success:
- UNI_Success:
- MUL_Success:
- WHO_Success:
- WC_Success:
- ID_REG_Success:
- MUTE_Success:
- MUTE_Already:
