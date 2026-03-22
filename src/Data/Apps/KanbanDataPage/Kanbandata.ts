type Task = {
  id: string;
  title: string;
  dueDate: string;
  comments: number;
  progress: string;
  image?: string;
};

type Column = {
  id: string;
  title: string;
  icon: string;
  tasks: Task[];
};

const kanbanData: Column[] = [
  {
    id: "column-1",
    title: "To Do",
    icon: "ph-list-bullets",
    tasks: [
      {
        id: "task-1",
        title: "Create homepage wireframe.",
        dueDate: "Nov 22",
        comments: 2,
        progress: "1/2",
      },
      {
        id: "task-2",
        title: "Draft new article content.",
        dueDate: "Dec 19",
        comments: 2,
        progress: "1/2",
        image: "/images/profile/07.jpg",
      },
      {
        id: "task-3",
        title: "Analyze client comments.",
        dueDate: "Sep 28",
        comments: 2,
        progress: "1/2",
      },
    ],
  },
  {
    id: "column-2",
    title: "In Progress",
    icon: "ph-chart-line-up",
    tasks: [
      {
        id: "task-4",
        title: "Prepare email marketing.",
        dueDate: "Jul 10",
        comments: 2,
        progress: "1/2",
        image: "/images/profile/10.jpg",
      },
    ],
  },
  {
    id: "column-3",
    title: "Review",
    icon: "ph-eye",
    tasks: [
      {
        id: "task-5",
        title: "Revise product listings.",
        dueDate: "Mar 27",
        comments: 2,
        progress: "1/2",
      },
      {
        id: "task-6",
        title: "Create initial app mockup.",
        dueDate: "Apr 09",
        comments: 2,
        progress: "1/2",
      },
    ],
  },
  {
    id: "column-4",
    title: "Done",
    icon: "ph-check-square-offset",
    tasks: [
      {
        id: "task-7",
        title: "Compile financial data.",
        dueDate: "Jul 24",
        comments: 2,
        progress: "1/2",
        image: "/images/profile/05.jpg",
      },
    ],
  },
  {
    id: "column-5",
    title: "Tested",
    icon: "ph-check-circle",
    tasks: [
      {
        id: "task-8",
        title: "Gather market insights.",
        dueDate: "Oct 04",
        comments: 2,
        progress: "1/2",
        image: "/images/profile/09.jpg",
      },
      {
        id: "task-9",
        title: "Improve page load times.",
        dueDate: "Aug 23",
        comments: 2,
        progress: "1/2",
      },
    ],
  },
];

export default kanbanData;
