export const activities = [
  {
    id: 1,
    iconColor: "text-light-primary",
    iconText: "W",
    username: "Wilson",
    activity: "added reaction in",
    post: "#product website",
    time: "09.00AM",
    images: [],
    actions: [],
  },
  {
    id: 2,
    iconColor: "text-light-info",
    iconText: "I",
    icon: "ph-image",
    activity: "2 image files and 2 videos uploaded",
    time: "12:45 PM",
    images: [
      "/images/draggable/02.jpg",
      "/images/draggable/04.jpg",
      "/images/draggable/01.jpg",
    ],
    actions: [],
  },
  {
    id: 3,
    iconColor: "text-light-success",
    iconText: "D",
    username: "Dane Wiza",
    activity: "added reaction in",
    post: "#product website",
    time: "09.00AM",
    content: "Need a feature...",
    badges: ["#🙂❤10Reactions", "#✨12Replies"],
    actions: [],
  },
  {
    id: 4,
    iconColor: "text-light-danger",
    iconText: "B",
    username: "Betty Mante",
    activity: "Request joined",
    post: "#reaserchteam groups",
    time: "4 days ago",
    actions: [
      { label: "Accept", color: "danger" },
      { label: "Rejects", color: "outline-danger" },
    ],
  },
  {
    id: 5,
    iconColor: "text-light-primary",
    iconText: "P",
    username: "Pinkie",
    activity: "uploaded",
    post: "2 attachments",
    time: "4 days ago",
    actions: [
      { label: "Accept", color: "primary" },
      { label: "Rejects", color: "outline-primary" },
    ],
  },
];

export const weekactivities = [
  {
    id: 1,
    name: "Dane Wiza",
    action: "added reaction in",
    post: "#product website",
    time: "09.00AM",
    content: "Need a feature",
    description:
      "Hello everyone, question on email marketing. What are some tips/tricks to avoid going to promotion spam/junk for automated marketing emails.",
    reactions: "#🙂❤10Reactions",
    replies: "#✨12Replies",
    iconColor: "text-light-success",
    iconText: "D",
    buttonText1: "Accept",
    buttonText2: "Rejects",
  },
  {
    id: 2,
    name: "Betty Mante",
    action: "Request joined",
    post: "#reaserchteam",
    time: "4 days ago",
    content: "",
    description: "",
    reactions: "",
    replies: "",
    iconColor: "text-light-danger",
    iconText: "B",
    buttonText1: "Accept",
    buttonText2: "Rejects",
  },
  {
    id: 3,
    name: "Pinkie",
    action: "uploaded",
    post: "#reaserchteam",
    time: "4 days ago",
    content: "uploaded 2 attachments",
    description: "",
    reactions: "",
    replies: "",
    iconColor: "text-light-primary",
    iconText: "P",
    buttonText1: "Accept",
    buttonText2: "Rejects",
  },
];

export const monthactivities = [
  {
    id: 1,
    user: "Dane Wiza",
    reaction: "added reaction in",
    post: "#product website",
    message:
      "Hello everyone, question on email marketing. What are some tips/tricks to avoid going to promotion spam/ junk for automated marketing emails",
    badges: ["#🙂❤10Reactions", "#✨12Replies"],
    time: "09:00AM",
    icon: "D",
  },
  {
    id: 2,
    user: "2 image files and 2 videos uploaded",
    icon: "ph-image",
    time: "Updated at 12:45 pm",
    images: [
      "/images/draggable/02.jpg",
      "/images/draggable/04.jpg",
      "/images/draggable/01.jpg",
    ],
  },
  {
    id: 3,
    user: "Pinkie",
    reaction: "uploaded",
    attachmentCount: 2,
    badge: "#reaserchteam",
    buttons: [
      { label: "Accept", class: "btn btn-primary" },
      { label: "Reject", class: "btn btn-outline-primary" },
    ],
    time: "4 days ago",
    icon: "P",
  },
];

export const devices = [
  {
    id: 1,
    deviceType: "Mac",
    deviceName: "Apple Mac 10.15.7",
    location: "Switzerland 201.36.24.108",
    status: "Online",
    statusColor: "text-success",
    icon: "ph-laptop",
  },
  {
    id: 2,
    deviceType: "Phone",
    deviceName: "Apple iPhone iOS 15.0.2",
    location: "Ukraine 176.38.19.14",
    status: "Offline",
    statusColor: "text-primary",
    icon: "ph-device-mobile",
  },
  {
    id: 3,
    deviceType: "Phone",
    deviceName: "Apple iPhone iOS 15.0.2",
    location: "Africa 176.49.19.13",
    status: "Offline",
    statusColor: "text-primary",
    icon: "ph-device-mobile",
  },
  {
    id: 4,
    deviceType: "Phone",
    deviceName: "Apple Mac 10.15.7",
    location: "America 201.136.24.108",
    status: "Offline",
    statusColor: "text-primary",
    icon: "ph-device-mobile",
  },
  {
    id: 5,
    deviceType: "Windows",
    deviceName: "Windows 10",
    location: "Portuguese 176.38.19.14",
    status: "Offline",
    statusColor: "text-primary",
    icon: "ph-device-mobile",
  },
];

export const privacyOptions = [
  {
    id: "option1",
    label: "Private",
    description: "Only users you choose can access",
    icon: "ph-lock",
    color: "bg-success",
    value: "option1",
  },
  {
    id: "option2",
    label: "Public",
    description: "Anyone with the link can access",
    icon: "ph-globe",
    color: "bg-primary",
    value: "option2",
  },
];

export const userOptions = [
  {
    id: "useroption1",
    label: "Users in the table",
    description: "Users in the table can sign in",
    icon: "ph-lock",
    color: "bg-primary",
    value: "useroption1",
  },
  {
    id: "useroption2",
    label: "Production team",
    description: "Team members can sign in",
    icon: "ph-lock",
    color: "bg-secondary",
    value: "useroption2",
  },
  {
    id: "useroption3",
    label: "Anyone from domain",
    description: "Users with your email domain can sign in",
    icon: "ph-lock",
    color: "bg-info",
    value: "useroption3",
  },
  {
    id: "useroption4",
    label: "Any email in table",
    description: "Anyone included in the table can sign in",
    icon: "ph-lock",
    color: "bg-danger",
    value: "useroption4",
  },
];

export interface Connection {
  id: string;
  title: string;
  description: string;
  imgSrc: string;
}

export const connections: Connection[] = [
  {
    id: "basic-switch-7",
    title: "GitHub",
    imgSrc: "/images/setting/geethub.png",
    description: "GitHub can be connected to various continuous integration",
  },
  {
    id: "basic-switch-8",
    title: "Slack",
    imgSrc: "/images/setting/slack.png",
    description: "Send notifications to channels and create various projects",
  },
  {
    id: "basic-switch-9",
    title: "Google",
    imgSrc: "/images/setting/google.png",
    description:
      "The core mission of Google is to organize the world's information.",
  },
  {
    id: "basic-switch-10",
    title: "Figma",
    imgSrc: "/images/setting/figma.png",
    description:
      "Figma is a web-based design tool focused on collaborative design.",
  },
  {
    id: "basic-switch-11",
    title: "Drive",
    imgSrc: "/images/setting/drive.png",
    description: "Google Drive is a comprehensive file storage and service.",
  },
  {
    id: "basic-switch-12",
    title: "Drop Box",
    imgSrc: "/images/setting/drop-box.png",
    description:
      "The service is designed to safeguard files from malfunctions.",
  },
  {
    id: "basic-switch-13",
    title: "Facebook",
    imgSrc: "/images/setting/facebook.png",
    description:
      "Facebook's journey from a university network to a global social media.",
  },
  {
    id: "basic-switch-14",
    title: "Instagram",
    imgSrc: "/images/setting/instagram.png",
    description:
      "Instagram's mission is to bring people closer to the things and people.",
  },
  {
    id: "basic-switch-15",
    title: "Twitter",
    imgSrc: "/images/setting/twitter.png",
    description: "Twitter, now known as X, is a social media platform.",
  },
  {
    id: "basic-switch-16",
    title: "Dribble",
    imgSrc: "/images/setting/dribble.png",
    description: "Dribbble is a self-promotion and social networking platform.",
  },
  {
    id: "basic-switch-17",
    title: "Linkedin",
    imgSrc: "/images/setting/linkdin.png",
    description: "LinkedIn boasts over 1 billion registered members globally.",
  },
  {
    id: "basic-switch-18",
    title: "Behance",
    imgSrc: "/images/setting/behance.png",
    description:
      "The platform allows creative professionals across various industries.",
  },
];

import { IconX, IconCode, IconArchive } from "@tabler/icons-react";
import { ReactNode } from "react";

export interface SecurityItem {
  icon: ReactNode;
  title: string;
  description: string;
  badge?: {
    icon: ReactNode;
    text: string;
    color: string; // ✅ Add this line
  };
  button?: {
    color: string;
    text: string;
  };
  rightText?: string;
}
export const securityItems: SecurityItem[] = [
  {
    icon: (
      <img
        src="/images/setting/google.png"
        alt="Google"
        className="w-35 h-35"
      />
    ),
    title: "Authentication",
    description:
      "It encompasses various methods to ensure that the person requesting access is indeed who they claim to be. Here are the key components and features of Google Authentication:",
    badge: {
      icon: <IconX size={16} className="me-1 text-primary" />,
      text: "Secondary",
      color: "light-secondary", // ✅ Add this
    },
    button: {
      color: "outline-success",
      text: "Turn off",
    },
  },
  {
    icon: (
      <div className="w-35 h-35 bg-primary d-flex align-items-center justify-content-center rounded-circle">
        <IconCode size={18} color="white" />
      </div>
    ),
    title: "Anti-Code",
    description:
      "An anti-phishing code is a security feature used by various online platforms, especially in financial and cryptocurrency services.",
    badge: {
      icon: <IconX size={16} className="me-1 text-primary" />,
      text: "Secondary",
      color: "light-secondary", // ✅ Add this
    },
    button: {
      color: "primary",
      text: "Turn On",
    },
  },
  {
    icon: (
      <div className="w-35 h-35 bg-success d-flex align-items-center justify-content-center rounded-circle">
        <IconArchive size={18} color="white" />
      </div>
    ),
    title: "Whitelist",
    description:
      "An anti-phishing code is a security feature used by various online platforms, especially in financial and cryptocurrency services.",
    rightText: "In development",
  },
];
export const leftSessionList = [
  {
    name: "Apple Mac 10.15.7",
    location: "Switzerland 201.36.24.108",
    iconClass: "ph-laptop",
    iconColor: "success",
    status: "online",
  },
  {
    name: "Apple iPhone iOS 15.0.2",
    location: "Ukraine 176.38.19.14",
    iconClass: "ph-device-mobile",
    iconColor: "primary",
    status: "offline",
  },
  {
    name: "Apple iPhone iOS 15.0.2",
    location: "Africa 176.49.19.13",
    iconClass: "ph-device-mobile",
    iconColor: "primary",
    status: "offline",
  },
];

export const rightSessionList = [
  {
    name: "Apple Mac 10.15.7",
    location: "America 201.136.24.108",
    iconClass: "ph-device-mobile",
    iconColor: "primary",
    status: "offline",
  },
  {
    name: "Windows 10",
    location: "Portuguese 176.38.19.14",
    iconClass: "ph-device-mobile",
    iconColor: "primary",
    status: "offline",
  },
];
