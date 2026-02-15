# Warehouse Simulation (Unity + ML-Agents)

A warehouse operations simulation built with Unity where multiple autonomous robots handle two competing workflows:

- **Customer order fulfillment** (pick from shelf → deliver to delivery zone)
- **Inventory restocking** (replenish low-stock shelves)

The project combines deterministic systems (task queues, priority scoring, NavMesh pathfinding, collision avoidance) with an ML-Agents-ready `RobotAgent` implementation for learned movement decisions.

---

## Project Overview

This simulation is designed to model key warehouse dynamics:

- Multi-robot coordination in a shared navigation space
- Dynamic task generation (orders and restocks)
- Priority-based dispatching under resource contention
- Live metrics tracking and dashboard visualization
- Optional reinforcement learning decision control for robots

### Current Scope

- Single main scene: `Assets/Scenes/Warehouse.unity`
- Runtime dashboard generated from code (no manual UI scene setup required)
- ML-Agents `Agent` implementation integrated in robot controller
- Episode-style learning telemetry for tracking improvement trends

---

## Setup Instructions

### Prerequisites

- **Unity 2022 LTS or newer** (recommended for stable ML-Agents + NavMesh workflows)
- **Python 3.10+** (for ML-Agents training CLI)
- Git

### 1) Clone the project

```bash
git clone <your-repo-url>
cd Warehouse-simulatation-
```

### 2) Open in Unity

1. Launch Unity Hub
2. Add this folder as a project
3. Open the project
4. Open the scene: `Assets/Scenes/Warehouse.unity`

### 3) Verify core scene objects (Inspector)

Make sure these runtime systems exist and are wired:

- `GameManager`
- `TaskManager`
- `RobotCoordinator`
- `LearningMetrics`
- `SimulationMetrics`

If some references are missing, assign them in the Inspector so orchestration and dashboard updates work correctly.

---

## ML-Agents Installation Steps

> Use a dedicated Python virtual environment for reproducibility.

### 1) Create and activate a virtual environment

**Windows (PowerShell):**

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
```

**macOS/Linux:**

```bash
python3 -m venv .venv
source .venv/bin/activate
```

### 2) Upgrade pip

```bash
python -m pip install --upgrade pip
```

### 3) Install ML-Agents packages

```bash
pip install mlagents mlagents-envs
```

### 4) Confirm installation

```bash
mlagents-learn --help
```

If this command fails, verify:

- virtual environment is activated
- compatible Python version is used
- Unity project includes ML-Agents package on the C# side

---

## How to Train Agents

Training can be run through Unity Editor play mode or a built player executable.

### 1) Configure behavior in Unity

On your robot prefab / `RobotAgent` object:

- Ensure a **Behavior Parameters** component exists
- Set behavior name (e.g., `WarehouseRobot`)
- Match action space with the agent implementation:
  - **Continuous actions = 3** (`moveX`, `moveZ`, `speedScale`)

### 2) Create a trainer config (YAML)

Example (`config/warehouse_ppo.yaml`):

```yaml
behaviors:
  WarehouseRobot:
    trainer_type: ppo
    hyperparameters:
      batch_size: 1024
      buffer_size: 10240
      learning_rate: 3.0e-4
      beta: 5.0e-4
      epsilon: 0.2
      lambd: 0.95
      num_epoch: 3
      learning_rate_schedule: linear
    network_settings:
      normalize: true
      hidden_units: 256
      num_layers: 2
    reward_signals:
      extrinsic:
        gamma: 0.99
        strength: 1.0
    max_steps: 5.0e6
    time_horizon: 128
    summary_freq: 20000
```

### 3) Start training

```bash
mlagents-learn config/warehouse_ppo.yaml --run-id=warehouse_ppo_v1
```

When prompted, press **Play** in Unity.

### 4) Monitor progress

Use TensorBoard:

```bash
tensorboard --logdir results
```

### 5) Deploy trained model

- Locate generated `.onnx` model in `results/<run-id>/`
- Assign it to the robot's Behavior Parameters model field
- Switch behavior type to inference mode for evaluation runs

---

## Controls Documentation

The project includes two control surfaces:

### 1) Runtime Dashboard Controls (UI)

- **Start simulation**: invokes `onStartSimulation`
- **Stop simulation**: invokes `onStopSimulation`
- **Reset simulation**: invokes `onResetSimulation`
- **Spawn new robot**: invokes `onSpawnRobot`
- **Adjust time scale** slider: updates `Time.timeScale`
- **Adjust order rate** slider: emits `onOrderRateChanged` event for task-rate tuning

### 2) Robot Input / Heuristic Controls

When heuristic mode is used for ML actions:

- **Horizontal axis** → movement X
- **Vertical axis** → movement Z
- **Left Shift** → speed boost action channel

### 3) Manual click-to-move test mode

`RobotAgent` supports click-to-move and random auto-move toggles for debugging, but spawned robots are configured by coordinator to disable these by default.

---

## Architecture Overview

High-level module structure:

- **Managers**
  - `GameManager`: simulation lifecycle and top-level wiring
  - `TaskManager`: queueing, prioritization, dispatch, completion handling
  - `RobotCoordinator`: robot spawning, roaming node assignment, contention resolution
  - `LearningMetrics`: episode summaries for RL-focused KPIs
  - `SimulationMetrics`: snapshot model + event notifications for dashboard
- **Robots**
  - `RobotAgent`: NavMesh motion, finite state transitions, ML observations/actions/rewards
- **Tasks**
  - `Order`, `RestockTask`, `IWarehouseTask`, `TaskPriority`
- **Inventory**
  - `Shelf`: stock levels, low-stock thresholds, restock trigger events
  - `InventoryItem`: item identity data
- **UI**
  - `WarehouseDashboardUI`: programmatic Canvas + controls + live KPI rendering

### Runtime Flow

1. `RobotCoordinator` spawns robots and builds navigation nodes.
2. `TaskManager` discovers shelves, listens for low-stock events, and generates random orders.
3. Priority scoring decides whether next assignment is delivery or restock.
4. `RobotAgent` executes shelf-to-delivery task lifecycle.
5. Completion data is pushed into `LearningMetrics`.
6. `SimulationMetrics` updates are consumed by `WarehouseDashboardUI`.

---

## Learning System Explanation

`RobotAgent` is implemented as an ML-Agents `Agent` and includes:

- **Observation space**
  - Normalized distance to target
  - Local target direction (x, z)
  - Nearby robot directional offsets + normalized distances (top-N neighbors)
  - Task-type one-hot vector: idle / order / restock

- **Action space (continuous, 3 channels)**
  - Move X direction
  - Move Z direction
  - Speed scaling

- **Reward shaping signals**
  - Idle-time penalty (encourages activity)
  - Collision penalties (robot and obstacle penalties)
  - Task completion reward
  - Time-sensitive completion bonus component

- **Episode behavior**
  - Internal reset logic via `OnEpisodeBegin`
  - Optional continuous operation (`MaxStep = 0`) when learning decisions are enabled

`LearningMetrics` tracks episode summaries including:

- Average task completion time
- Collision count
- Path efficiency (actual distance / optimal distance)
- Throughput per hour

These can be graphed externally (e.g., TensorBoard custom metrics or exported logs).

---

## Key Algorithms Used

1. **Priority queue scoring (task dispatch)**
   - Combined score from task priority enum + task age
   - Configurable restock weighting multiplier
   - Delivery streak guard to avoid starving restocks

2. **Nearest-node assignment for idle roaming**
   - Chooses nearest unoccupied navigation node
   - Skips trivial reassignments within minimum distance

3. **Node contention resolution**
   - Competing robots for same node resolved by NavMesh avoidance priority
   - Lower-priority robot enters short yielding state

4. **Low-stock event-driven replenishment**
   - Shelf raises event on low-stock transition
   - Task manager enqueues restock while deduplicating pending shelves

5. **Learning telemetry efficiency metric**
   - Compares actual path distance against geometric optimal path estimate

---

## Performance Optimization Techniques

The current codebase includes several practical optimization strategies:

- Reuses persistent list buffers for candidate shelf selection to reduce GC allocations during frequent order generation
- Uses squared-distance comparisons for navigation node evaluation to avoid unnecessary square-root cost
- Restricts random assignment cadence with configurable `assignmentInterval` in `FixedUpdate`
- Guards logging behind debug flags to avoid per-frame log overhead
- Clears long-lived collections in `OnDestroy` to prevent stale references across long sessions/reloads
- Uses bounded robot-neighbor observation count for ML state size control

Potential next optimizations:

- Move task queue selection to heap-based priority structures for larger workloads
- Use Unity Jobs/ECS for very high robot counts
- Batch physics/overlap queries for obstacle and neighbor checks

---

## Known Limitations

- `PerformanceMetrics` is currently a scaffold and does not yet publish full production KPI aggregation.
- Dashboard control events (`onStartSimulation`, etc.) require explicit Inspector wiring to simulation logic.
- No trainer YAML is committed by default; training profile must be authored per experiment.
- Reward shaping values are static Inspector parameters and may require retuning per map layout.
- System currently assumes a single-scene workflow and does not include save/load for warehouse state.
- Very large robot counts may saturate NavMesh and physics overlap checks without additional optimization.

---

## Repository Structure

```text
Assets/
├── Scenes/
│   └── Warehouse.unity
├── Scripts/
│   ├── Inventory/
│   ├── Managers/
│   ├── Robots/
│   ├── Tasks/
│   └── UI/
└── README.md
```

---

## Quick Start Checklist

1. Open `Warehouse.unity`
2. Verify manager references
3. Press Play and confirm dashboard appears
4. Verify robots spawn and tasks are assigned
5. (Optional) Enable ML behavior + start `mlagents-learn`

