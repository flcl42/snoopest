
# Snoopest

## How to run

From shell

```bash
./snoopest 8546,localhost:8545 8552,localhost:8551 4001,localhost:4000 8553,localhost:8550
```

From docker

```bash
docker run flcl42/snoopest 8546,localhost:8545 8552,localhost:8551 4001,localhost:4000 8553,localhost:8550
```

## Troubleshooting

'Access denied.' on Windows. Need to add port using powershell with admin permissions: `netsh http add urlacl url=http://*:8546/ user=$ENV:USERNAME` or run the tool as admin.<br>
