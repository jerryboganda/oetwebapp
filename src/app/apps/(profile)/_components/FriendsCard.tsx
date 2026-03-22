import React from "react";
import { Card, CardBody, CardHeader } from "reactstrap";
import { IconUserCheck } from "@tabler/icons-react";

const FriendsCard = () => {
  const friends = [
    {
      name: "Bette Hagenes",
      title: "Wed Developer",
      image: "/images/avatar/2.png",
      bgColor: "bg-dark",
    },
    {
      name: "Fleta Walsh",
      title: "Wed Designer",
      image: "/images/avatar/10.png",
      bgColor: "bg-primary",
    },
    {
      name: "Lenora",
      title: "UI/UX designer",
      image: "/images/avatar/14.png",
      bgColor: "bg-success",
    },
    {
      name: "Fleta Walsh",
      title: "React Developer",
      image: "/images/avatar/16.png",
      bgColor: "bg-warning",
    },
    {
      name: "Emery McKenzie",
      title: "Wed Developer",
      image: "/images/avatar/13.png",
      bgColor: "bg-danger",
    },
    {
      name: "Bette Hagenes",
      title: "Wed Designer",
      image: "/images/avatar/1.png",
      bgColor: "bg-info",
    },
  ];

  return (
    <Card className="d-lg-block d-none">
      <CardHeader>
        <h5 className="card-header">Friends</h5>
      </CardHeader>
      <CardBody>
        <div className="profile-friends">
          {friends.map((friend, index) => (
            <div key={index} className="d-flex align-items-center mt-3">
              <div
                className={`h-40 w-40 d-flex-center b-r-50 overflow-hidden ${friend.bgColor}`}
              >
                <img
                  src={friend.image}
                  alt={friend.name}
                  className="img-fluid"
                />
              </div>
              <div className="flex-grow-1 ps-2">
                <div className="fw-medium">{friend.name}</div>
                <div className="text-muted f-s-12">{friend.title}</div>
              </div>
              <button className="btn icon-btn btn btn-light-primary b-r-22">
                <IconUserCheck size={20} />
              </button>
            </div>
          ))}
        </div>
      </CardBody>
    </Card>
  );
};

export default FriendsCard;
