# Python 예시 게임: 구조 · 저장 · 테스트

이 문서는 정리 전 `python/docs/`의 개발·배포·저장 관련 문서에서 **현재 예시 게임을 이해하고 실행하는 데 필요한 부분**만 모은 요약이야. 상세 구현의 최종 근거는 실제 `python/neonvoid/` 코드와 테스트이고, 이 문서는 새 Unity/Godot 게임의 확정 기술 설계가 아니야.

## 1. 런타임과 코드 구조

- Python 3.11 이상, `tkinter`와 표준 라이브러리 사용. 외부 런타임 패키지 불필요.
- 실행 진입점: `python/neon_void.py`, `python/neonvoid/cli.py`, `python -m neonvoid`.
- `game.py`: 고정 스텝 게임 루프, 전투, 적 AI, 입력·상태 관리.
- `view.py`, `display.py`, `render_pool.py`: Tk Canvas, HUD, 화면 배율, 드로잉 풀.
- `entities.py`, `combat.py`, `controls.py`: 플레이어·적·탄환, 피해·상태이상, 이동·대시.
- `content/enemies.py`, `content/upgrades.py`, `content/sectors.py`, `content/stages.py`: 적, 소프트웨어, 구역·웨이브 데이터.
- `world/session.py`, `infection.py`, `extraction.py`: 허브/구역 진행, 감염·정화, 철수.
- `metaprogression.py`, `database_storage.py`, `os_checkpoint.py`, `profile.py`, `settings.py`, `storage.py`: 영구 성장·체크포인트·설정·기록.
- `audio.py`: 절차적 오디오, Windows 사운드 폴백.

## 2. 게임의 기본 흐름

백신 허브에서 OS를 배포 → CPU / MEMORY / STORAGE / NETWORK 구역을 자유 순서로 정화 → OS CLEAN 상태에서 철수 보상을 확정 → 영구 백신 데이터베이스에 반영. 이 **Python 참고 게임의 4개 구역**은 과거 GDD가 다루는 **새 3D 게임의 5개 하드웨어**와 다르니 혼동하지 마.

Python 소스 기준 60Hz 고정 업데이트와 시드 기반 테스트 경로를 사용해. 보상과 상태 데이터는 게임의 실제 원자적 저장 경로를 통과해야 하며, 설정·세이브를 함부로 초기화하면 안 돼.

## 3. 사용자 데이터 보존

소스 실행의 기본 저장 경로는 **프로젝트 최상위 폴더**이고, 다음 다섯 JSON은 실행 중 생성될 수 있어.

| 파일 | 내용 |
|---|---|
| `save.json` | 최고 점수·웨이브 등 요약 |
| `settings.json` | 입력 키, 그래픽/음량 환경설정 |
| `profile.json` | 누적 기록과 플레이어 프로필 |
| `antivirus_db.json` | 영구 백신 성장 및 소프트웨어 보유 |
| `os_checkpoint.json` | 진행 중인 운영체제·웨이브 복구 정보 |

실행 중인 게임의 저장 파일을 덮어쓰지 마. 실제 사용자 데이터를 테스트에 사용하지 말고 테스트 전용 임시 폴더/fixture를 사용해. 정리본 ZIP에는 실제 사용자 세이브를 담지 않았어. 기존 소스에서 실행한 저장 파일이 있다면 수동으로 백업해서 보관해야 해.

Windows의 독립 실행 EXE로 직접 빌드하면 저장 경로는 `%LOCALAPPDATA%\NeonVoid`이고, 이때 사용되는 경로는 소스 실행과 달라. 이 설정은 `runtime_paths.py`에서 관리해.

## 4. 실행 및 검증

```powershell
cd python
py -3 neon_void.py
py -3 -m unittest discover -s tests -v
py -3 neon_void.py --selftest 180 --no-sound --seed 42 --god
```

자동 전투 selftest도 Tk 창을 생성하므로 화면 환경이 필요해. Linux의 헤드리스 환경에서는 별도의 가상 디스플레이(Xvfb)가 필요할 수 있어. GUI 수동 품질 검수와 음향 재생 검증은 유닛 테스트 통과만으로 대신할 수 없어.

## 5. 수동 Windows EXE 빌드 (선택)

ZIP 안에 바이너리·가상환경·CI 워크플로는 포함되어 있지 않아. 로컬 Windows에서 필요한 경우:

```powershell
cd python
py -3 -m pip install "pyinstaller>=6.16,<7"
py -3 -m unittest discover -s tests -v
py -3 -m PyInstaller --noconfirm --clean --onefile --windowed --name NeonVoid neon_void.py
```

산출물은 `python/dist/NeonVoid.exe`야. 실제 배포 전에는 Windows에서 실행·저장·음악·입력·창 모드를 수동 검증해야 해.

## 6. 보존 범위와 새 프로젝트 관계

이 정리본은 **Python 게임을 코드·테스트와 함께 참고 자료로 보존**하기 위한 것이야. 과거 Godot 전환 작업의 `scenes/`, `scripts/`, 빌드 캐시, 마이그레이션 전용 테스트와 CI는 제외했어. [기존 기획서](NEON_VOID_GDD_V1.md)의 `[확정]`, `[초안]`, `[미확정]` 구분은 원문 그대로 보존됐지만, 다음 엔진의 신규 명세를 자동으로 결정하는 근거로 사용해서는 안 돼.
