import React, { useState } from "react";
import {
  Card,
  CardBody,
  Nav,
  NavItem,
  NavLink,
  Badge,
  CardHeader,
  Button,
  Row,
  Col,
} from "reactstrap";
import classnames from "classnames";
import {
  IconUser,
  IconTimeline,
  IconClipboardData,
  IconPhotoHeart,
  IconUsers,
  IconUserCheck,
  IconHeart,
  IconBrandHipchat,
  IconSend,
  IconDotsVertical,
} from "@tabler/icons-react";

interface TabItem {
  id: string;
  title: string;
  icon: React.ReactNode;
  badge?: string;
}
interface Friend {
  id: number;
  name: string;
  title: string;
  avatar: string;
  bgClass: string;
}

const tabs: TabItem[] = [
  {
    id: "1",
    title: "Profile",
    icon: <IconUser className="me-2 fw-bold" size={18} />,
  },
  {
    id: "2",
    title: "Activities",
    icon: <IconTimeline className="me-2 fw-bold" size={18} />,
    badge: "10+",
  },
  {
    id: "3",
    title: "Projects",
    icon: <IconClipboardData className="me-2 fw-bold" size={18} />,
  },
  {
    id: "4",
    title: "Post",
    icon: <IconPhotoHeart className="me-2 fw-bold" size={18} />,
  },
  {
    id: "5",
    title: "Friends",
    icon: <IconUsers className="me-2 fw-bold" size={18} />,
  },
];

const friends: Friend[] = [
  {
    id: 1,
    name: "Bette Hagenes",
    title: "Wed Developer",
    avatar: "/images/avatar/2.png",
    bgClass: "bg-dark",
  },
  {
    id: 2,
    name: "Fleta Walsh",
    title: "Wed Designer",
    avatar: "/images/avatar/10.png",
    bgClass: "bg-primary",
  },
  {
    id: 3,
    name: "Lenora",
    title: "UI/UX designer",
    avatar: "/images/avatar/14.png",
    bgClass: "bg-success",
  },
  {
    id: 4,
    name: "Fleta Walsh",
    title: "React Developer",
    avatar: "/images/avatar/16.png",
    bgClass: "bg-warning",
  },
  {
    id: 5,
    name: "Emery McKenzie",
    title: "Wed Developer",
    avatar: "/images/avatar/13.png",
    bgClass: "bg-danger",
  },
  {
    id: 6,
    name: "Bette Hagenes",
    title: "Wed Designer",
    avatar: "/images/avatar/1.png",
    bgClass: "bg-info",
  },
];

const galleryImages: string[] = [
  "/images/profile/19.jpg",
  "/images/profile/27.jpg",
  "/images/profile/28.jpg",
  "/images/profile/29.jpg",
  "/images/profile/30.jpg",
];

const postActions = [
  { icon: <IconHeart size={18} />, label: "Like" },
  { icon: <IconBrandHipchat size={18} />, label: "Comment" },
  { icon: <IconSend size={18} />, label: "Share" },
];

const ProfileTabs: React.FC = () => {
  const [activeTab, setActiveTab] = useState("1");

  return (
    <>
      <Col lg="3">
        <Card id="profile-tabs">
          <CardBody>
            <div className="tab-wrapper">
              <Nav
                tag="ul"
                className="profile-app-tabs flex-column border-0 m-0 p-0"
                tabs
              >
                {tabs.map((tab) => (
                  <NavItem tag="li" key={tab.id} className="w-100 d-flex">
                    <NavLink
                      className={classnames(
                        "tab-link fw-medium f-s-16 f-w-600 d-block w-100",
                        {
                          active: activeTab === tab.id,
                        }
                      )}
                      onClick={() => setActiveTab(tab.id)}
                      data-tab={tab.id}
                    >
                      {tab.icon}
                      {tab.title}
                      {tab.badge && (
                        <Badge
                          color="warning"
                          pill
                          className="badge-notification ms-2"
                        >
                          {tab.badge}
                          <span className="visually-hidden">
                            unread messages
                          </span>
                        </Badge>
                      )}
                    </NavLink>
                  </NavItem>
                ))}
              </Nav>
            </div>
          </CardBody>
        </Card>
        <Card className="d-lg-block d-none" id="friend">
          <CardHeader>
            <h5>Friends</h5>
          </CardHeader>
          <CardBody className="profile-friends">
            {friends.map((friend) => (
              <div className="d-flex align-items-center mt-3" key={friend.id}>
                <div
                  className={`h-40 w-40 d-flex-center b-r-50 overflow-hidden ${friend.bgClass}`}
                >
                  <img
                    src={friend.avatar}
                    alt={friend.name}
                    className="img-fluid"
                  />
                </div>
                <div className="flex-grow-1 ps-2">
                  <div className="fw-medium">{friend.name}</div>
                  <div className="text-muted f-s-12">{friend.title}</div>
                </div>
                <Button
                  href="#"
                  color="light-secondary"
                  className="icon-btn b-r-22"
                >
                  <IconUserCheck size={18} />
                </Button>
              </div>
            ))}
          </CardBody>
        </Card>
        <Card>
          <CardBody>
            <div className="d-flex align-items-center">
              <div className="h-45 w-45 d-flex-center b-r-50 overflow-hidden bg-danger">
                <img
                  src="/images/avatar/16.png"
                  alt="avatar"
                  className="img-fluid"
                />
              </div>
              <div className="flex-grow-1 ps-2 pe-2">
                <div className="f-w-600">Heli Walsh</div>
                <div className="text-muted f-s-12">3 Week</div>
              </div>
              <div>
                <IconDotsVertical size={18} />
              </div>
            </div>
            <div className="post-div">
              <Row className="g-2 my-2">
                {galleryImages.map((imgSrc, idx) => (
                  <Col
                    xs={galleryImages.length > 2 && idx > 1 ? 4 : 6}
                    key={idx}
                  >
                    <img
                      src={imgSrc}
                      alt={`img-${idx}`}
                      className="w-100 rounded"
                    />
                  </Col>
                ))}
              </Row>
              <p className="text-muted">
                There&#39;s nothing like fresh flowers!......🌸🌼🌻
              </p>
              <div className="post-icon d-flex align-items-center gap-2">
                {postActions.map((action, idx) => (
                  <span key={idx}>{action.icon}</span>
                ))}
                <p className="text-secondary mb-0 ms-2">2k Likes</p>
              </div>
            </div>
          </CardBody>
        </Card>
      </Col>
    </>
  );
};

export default ProfileTabs;
