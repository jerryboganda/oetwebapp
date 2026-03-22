import React from "react";
import { Card, CardBody, UncontrolledTooltip } from "reactstrap";
import {
  DotsThreeVertical,
  FacebookLogo,
  Heart,
  InstagramLogo,
  MessengerLogo,
  ShareNetwork,
  Star,
  WhatsappLogo,
} from "phosphor-react";

interface Bookmark {
  id: number;
  title: string;
  url: string;
  image: string;
  isFavourite?: boolean;
  isShared?: boolean;
  isStarred?: boolean;
  isDelete?: boolean;
}

interface BookCardProps {
  bookmark: Bookmark;
  onDelete: (id: number) => void;
  onFavouriteToggle: (id: number) => void;
  onShareToggle?: (id: number) => void;
  onStarToggle?: (id: number) => void;
  onEdit?: (bookmark: Bookmark) => void;
}

const BookCard: React.FC<BookCardProps> = ({
  bookmark,
  onDelete,
  onFavouriteToggle,
  onShareToggle,
  onStarToggle,
  onEdit,
}) => {
  return (
    <Card className="book-mark-card">
      <CardBody>
        <div className="draggable-card-img">
          <img
            src={bookmark?.image}
            alt={bookmark?.title}
            className="h-225 img-fluid"
          />
          <div className="video-transparent-box" />
          <div className="draggable-card-icon">
            <div
              className="bg-white h-35 w-35 d-flex-center b-r-50 me-3 heartBtn mb-2 "
              onClick={() => onFavouriteToggle(bookmark?.id)}
            >
              <Heart
                size={18}
                weight={bookmark?.isFavourite ? "fill" : "bold"}
                className="text-danger f-s-18"
              />
            </div>
            <div
              id={"share_" + bookmark.id}
              className="bg-white h-35 w-35 d-flex-center b-r-50 me-3 shareBtn mb-2"
              onClick={() => {
                if (onShareToggle) {
                  onShareToggle(bookmark.id);
                }
              }}
            >
              <ShareNetwork size={18} className="f-s-18 text-primary" />
              {/* Share Dropdown */}
              <UncontrolledTooltip
                className=" bg-white"
                placement="right-start"
                target={"share_" + bookmark.id}
              >
                <div className="d-flex justify-content-around py-2 ">
                  <WhatsappLogo
                    size={18}
                    weight="duotone"
                    className="text-primary  ms-2"
                  />
                  <InstagramLogo
                    size={18}
                    weight="duotone"
                    className="text-success  ms-2"
                  />
                  <FacebookLogo
                    size={18}
                    weight="duotone"
                    className="text-info  ms-2"
                  />
                  <MessengerLogo
                    size={18}
                    weight="duotone"
                    className=" text-danger ms-2"
                  />
                </div>
              </UncontrolledTooltip>
            </div>
            <div
              className="bg-white h-35 w-35 d-flex-center b-r-50 me-3 starBtn mb-2"
              onClick={() => {
                if (onStarToggle) {
                  onStarToggle(bookmark.id);
                }
              }}
            >
              <span className="f-s-18 text-warning">
                <Star
                  size={18}
                  weight={bookmark.isStarred ? "fill" : "bold"}
                  className="text-warning f-s-18"
                />
              </span>
            </div>
          </div>
          {/* More actions */}
          <div className="dropdown action-icon">
            <DotsThreeVertical size={18} weight="bold" className="text-white" />
            <ul className="dropdown-menu dropdown-menu-end">
              <li>
                <a
                  className="dropdown-item"
                  href="#"
                  onClick={() => {
                    if (onEdit) {
                      onEdit(bookmark);
                    }
                  }}
                >
                  <i className="ti ti-edit text-success"></i> Edit
                </a>
              </li>
              <li>
                <a
                  className="dropdown-item deletbtn"
                  href="#"
                  onClick={() => onDelete(bookmark.id)}
                >
                  <i className="ti ti-trash text-danger"></i> Delete
                </a>
              </li>
            </ul>
          </div>
        </div>
        <div className="draggable-card-content pt-4">
          <h5 className="mb-2">{bookmark.title}</h5>
          <p>{bookmark.url}</p>
        </div>
      </CardBody>
    </Card>
  );
};

export default BookCard;
