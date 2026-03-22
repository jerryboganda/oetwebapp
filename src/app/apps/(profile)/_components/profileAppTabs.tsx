import React, { SetStateAction } from "react";
import { Card, CardBody, Nav, NavItem, NavLink, Badge } from "reactstrap";
import {
  IconUser,
  IconTimeline,
  IconClipboardData,
  IconPhotoHeart,
  IconUsers,
} from "@tabler/icons-react";
import classNames from "classnames";

interface ProfileTabTypes {
  data: string;
  setData: React.Dispatch<SetStateAction<string>>;
}

const tabs = [
  { id: "1", label: "Profile", icon: <IconUser size={18} /> },
  {
    id: "2",
    label: "Activities",
    icon: <IconTimeline size={18} />,
    badge: "10+",
  },
  { id: "3", label: "Projects", icon: <IconClipboardData size={18} /> },
  { id: "4", label: "Post", icon: <IconPhotoHeart size={18} /> },
  { id: "5", label: "Friends", icon: <IconUsers size={18} /> },
];

const ProfileTabs: React.FC<ProfileTabTypes> = ({ data, setData }) => {
  return (
    <Card>
      <CardBody>
        <Nav tabs className="profile-app-tabs border-0">
          {tabs.map((tab) => (
            <NavItem key={tab.id} className="w-100 d-flex nav-item">
              <NavLink
                className={classNames(
                  "tab-link fw-medium f-s-16 f-w-600 w-100 d-block",
                  {
                    active: data === tab.id,
                  }
                )}
                onClick={() => setData(tab.id)}
              >
                {tab.icon}
                <span className="ms-1">
                  {tab.label}
                  {tab.badge && (
                    <Badge
                      color="warning"
                      pill
                      className="badge-notification ms-2"
                    >
                      {tab.badge}
                      <span className="visually-hidden">unread messages</span>
                    </Badge>
                  )}
                </span>
              </NavLink>
            </NavItem>
          ))}
        </Nav>
      </CardBody>
    </Card>
  );
};

export default ProfileTabs;
