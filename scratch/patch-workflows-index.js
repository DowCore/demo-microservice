const fs = require('fs');
const filePath = 'D:\\Project\\vue-demo\\apps\\web-antd\\src\\views\\orchestration\\workflows\\index.vue';
let content = fs.readFileSync(filePath, 'utf8');

const oldCompleteTask = `async function completeTask(task: WorkflowTask, pass: boolean) {
  const opinion = opinions[task.id]?.trim();
  try {
    const instance = await completeWorkflowTaskApi(task.id, {
      pass,
      opinion: opinion || undefined,
    });
    message.success(completeHint(instance.status, pass, instance.result));
    await openTasks();
  } catch {
    /* 拦截器已提示 */
  }
}`;

const newCompleteTask = `const transferUsers = reactive<Record<string, string>>({});
const showTransfer = reactive<Record<string, boolean>>({});

async function executeTaskAction(
  task: WorkflowTask,
  action: 'approve' | 'reject_to_prev' | 'reject_to_starter' | 'reject_terminate' | 'transfer',
) {
  const opinion = opinions[task.id]?.trim();
  const transferUser = transferUsers[task.id]?.trim();

  if (action === 'transfer' && !transferUser) {
    message.warning('请输入转办目标用户名');
    return;
  }

  try {
    const instance = await completeWorkflowTaskApi(task.id, {
      action,
      pass: action === 'approve',
      opinion: opinion || undefined,
      transferUserName: action === 'transfer' ? transferUser : undefined,
    });
    if (action === 'approve') {
      message.success(completeHint(instance.status, true, instance.result));
    } else if (action === 'transfer') {
      message.success(\`已转办给 \${transferUser}\`);
    } else if (action === 'reject_to_starter') {
      message.success('已退回给发起人修改');
    } else if (action === 'reject_to_prev') {
      message.success('已驳回至上一审批节点');
    } else {
      message.warning('已彻底终止作废该流程');
    }
    showTransfer[task.id] = false;
    await openTasks();
  } catch {
    /* 拦截器已提示 */
  }
}

async function completeTask(task: WorkflowTask, pass: boolean) {
  await executeTaskAction(task, pass ? 'approve' : 'reject_terminate');
}`;

content = content.replace(oldCompleteTask, newCompleteTask);

const oldDrawerButtons = `          <Space class="mt-2">
            <Button
              size="small"
              type="primary"
              @click="completeTask(task, true)"
            >
              同意
            </Button>
            <Button danger size="small" @click="completeTask(task, false)">
              驳回
            </Button>
          </Space>`;

const newDrawerButtons = `          <div v-if="showTransfer[task.id]" class="mt-2 flex items-center gap-2">
            <Input
              v-model:value="transferUsers[task.id]"
              size="small"
              placeholder="请输入接手人用户名"
              class="flex-1"
            />
            <Button size="small" type="primary" @click="executeTaskAction(task, 'transfer')">确认转办</Button>
            <Button size="small" @click="showTransfer[task.id] = false">取消</Button>
          </div>
          <Space wrap class="mt-2">
            <Button
              size="small"
              type="primary"
              @click="executeTaskAction(task, 'approve')"
            >
              同意
            </Button>
            <Button
              size="small"
              @click="executeTaskAction(task, 'reject_to_prev')"
            >
              驳回上一步
            </Button>
            <Button
              size="small"
              @click="executeTaskAction(task, 'reject_to_starter')"
            >
              退回发起人
            </Button>
            <Button
              size="small"
              @click="showTransfer[task.id] = !showTransfer[task.id]"
            >
              转办
            </Button>
            <Button
              danger
              size="small"
              @click="executeTaskAction(task, 'reject_terminate')"
            >
              终止作废
            </Button>
          </Space>`;

content = content.replace(oldDrawerButtons, newDrawerButtons);

// Make drawer width slightly wider for richer actions
content = content.replace(`width="480"`, `width="540"`);

fs.writeFileSync(filePath, content, 'utf8');
console.log('Successfully patched workflows/index.vue');
