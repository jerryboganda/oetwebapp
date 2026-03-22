"use client";
import React, { useState, ChangeEvent } from "react";
import {
  Button,
  Card,
  CardBody,
  Col,
  Container,
  Form,
  FormGroup,
  Input,
  Label,
  Modal,
  ModalBody,
  ModalFooter,
  ModalHeader,
  Nav,
  NavItem,
  NavLink,
  Row,
  TabContent,
  TabPane,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  BookBookmark,
  Bookmark,
  HeartStraight,
  ShareNetwork,
  Star,
  Trash,
  UserCircle,
  Tag,
} from "phosphor-react";
import BookCard from "@/app/apps/(bookmark)/_components/BookCard";
import { bookMarkTableData } from "@/Data/Apps/BookmarkDataPage/Bookmark1";
import { IconCircleFilled, IconStack2 } from "@tabler/icons-react";

// Define the types for the bookmark
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

const BookmarkPage = () => {
  const [activeTab, setActiveTab] = useState<string>("tab1");
  const [modalOpen, setModalOpen] = useState<boolean>(false);
  const [bookmarksData, setBookmarksData] =
    useState<Bookmark[]>(bookMarkTableData); // assuming bookMarkTableData is defined somewhere
  const [title, setTitle] = useState<string>("");
  const [url, setUrl] = useState<string>("");
  const [file, setFile] = useState<File | null>(null);
  const [editModalOpen, setEditModalOpen] = useState<boolean>(false);
  const [editBookmark, setEditBookmark] = useState<Bookmark | null>(null);

  const toggleEditModal = () => setEditModalOpen(!editModalOpen);
  const toggleModal = () => setModalOpen(!modalOpen);

  const handleFileChange = (event: ChangeEvent<HTMLInputElement>) => {
    if (event.target.files) {
      setFile(event.target.files[0] || null);
    }
  };

  // Handle bookmark add
  const handleAddBookmark = () => {
    const newBookmark: Bookmark = {
      id: bookmarksData.length + 1,
      title,
      url,
      image: file ? URL.createObjectURL(file) : "",
    };

    setBookmarksData([newBookmark, ...bookmarksData]);
    setTitle("");
    setUrl("");
    setFile(null);
    setModalOpen(false);
  };

  const handleDelete = (id: number) => {
    setBookmarksData(
      bookmarksData.map((bookmark) =>
        bookmark.id === id
          ? { ...bookmark, isDelete: !bookmark.isDelete }
          : bookmark
      )
    );
  };

  const handleFavouriteToggle = (id: number) => {
    setBookmarksData(
      bookmarksData.map((bookmark) =>
        bookmark.id === id
          ? { ...bookmark, isFavourite: !bookmark.isFavourite }
          : bookmark
      )
    );
  };

  const handleShareToggle = (id: number) => {
    setBookmarksData(
      bookmarksData.map((bookmark) =>
        bookmark.id === id
          ? { ...bookmark, isShared: !bookmark.isShared }
          : bookmark
      )
    );
  };

  const handleStarToggle = (id: number) => {
    setBookmarksData(
      bookmarksData.map((bookmark) =>
        bookmark.id === id
          ? { ...bookmark, isStarred: !bookmark.isStarred }
          : bookmark
      )
    );
  };

  const handleEditClick = (bookmark: Bookmark) => {
    setEditBookmark(bookmark);
    setTitle(bookmark.title);
    setUrl(bookmark.url);
    setEditModalOpen(true);
  };

  const handleSaveChanges = () => {
    const updatedBookmark: Bookmark = {
      id: editBookmark?.id ?? 0,
      title,
      url,
      image: file ? URL.createObjectURL(file) : (editBookmark?.image ?? ""),
    };

    const updatedBookmarks = bookmarksData.map((bookmark) =>
      bookmark.id === editBookmark?.id ? updatedBookmark : bookmark
    );

    setBookmarksData(updatedBookmarks);
    resetForm();
    toggleEditModal();
  };

  const resetForm = () => {
    setTitle("");
    setUrl("");
    setFile(null);
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Bookmark"
          title="Apps"
          path={["Bookmark"]}
          Icon={IconStack2}
        />
        <Row>
          <Col lg={3}>
            <Card>
              <CardBody>
                <div className="vertical-tab setting-tab">
                  <Nav tabs className="nav nav-tabs tab-light-primary m-0">
                    <NavItem className="cursor-pointer">
                      <NavLink
                        className={activeTab === "tab1" ? "active" : ""}
                        onClick={() => setActiveTab("tab1")}
                      >
                        <Bookmark size={30} className="pe-2" />
                        Book Mark
                      </NavLink>
                    </NavItem>
                    <NavItem className="cursor-pointer">
                      <NavLink
                        className={activeTab === "tab2" ? "active" : ""}
                        onClick={() => setActiveTab("tab2")}
                      >
                        <HeartStraight size={30} className="pe-2" />
                        Favourites
                      </NavLink>
                    </NavItem>
                    <NavItem className="cursor-pointer">
                      <NavLink
                        className={activeTab === "tab3" ? "active" : ""}
                        onClick={() => setActiveTab("tab3")}
                      >
                        <ShareNetwork size={30} className="pe-2" />
                        Share
                      </NavLink>
                    </NavItem>
                    <NavItem className="cursor-pointer">
                      <NavLink
                        className={activeTab === "tab4" ? "active" : ""}
                        onClick={() => setActiveTab("tab4")}
                      >
                        <Star size={30} className="pe-2" />
                        Important
                      </NavLink>
                    </NavItem>
                    <NavItem className="cursor-pointer">
                      <NavLink
                        className={activeTab === "tab5" ? "active" : ""}
                        onClick={() => setActiveTab("tab5")}
                      >
                        <Trash size={30} className="pe-2" />
                        Delete
                      </NavLink>
                    </NavItem>
                  </Nav>
                </div>

                <div className="app-divider-v pt-2 pb-2 dashed"></div>

                <ul className="email-list">
                  <li className="cursor-pointer f-w-500 text-dark">
                    <h6>Labels</h6>
                  </li>
                  <li className="cursor-pointer f-w-500 text-dark">
                    <IconCircleFilled className=" pe-2 text-danger" /> Social
                  </li>
                  <li className="cursor-pointer f-w-500 text-dark">
                    <IconCircleFilled className=" pe-2 text-primary" /> Company
                  </li>
                  <li className="cursor-pointer f-w-500 text-dark">
                    <IconCircleFilled className=" pe-2 text-success" />{" "}
                    Important
                  </li>
                  <li className="cursor-pointer f-w-500 text-dark">
                    <IconCircleFilled className=" pe-2 text-info" /> Private
                  </li>
                </ul>
                <div className="app-divider-v pt-2 pb-2 dashed"></div>

                {/* Bookmark Options */}
                <ul className="email-list">
                  <li className="f-w-500 text-dark">
                    <Bookmark size={30} className="pe-2" />
                    All Bookmark
                  </li>
                  <li className="f-w-500 text-dark">
                    <BookBookmark size={30} className="pe-2" /> Primary
                  </li>
                  <li className="f-w-500 text-dark">
                    <Tag size={30} className="pe-2" />
                    Promotions
                  </li>
                  <li className="f-w-500 text-dark">
                    <UserCircle size={30} className="pe-2" />
                    Social
                  </li>
                </ul>

                {/* Add Bookmark Button */}
                <Button
                  color="primary"
                  className="btn btn-light-primary w-100 mt-4 rounded"
                  block
                  onClick={toggleModal}
                >
                  Add Bookmark
                </Button>
              </CardBody>
            </Card>
          </Col>
          <Col lg={9}>
            <TabContent activeTab={activeTab}>
              <TabPane tabId="tab1">
                <Row className="bookmark-card">
                  {bookmarksData
                    .filter(
                      (bookmark) =>
                        !bookmark.isStarred &&
                        !bookmark.isShared &&
                        !bookmark.isDelete
                    )
                    .map((bookmark) => (
                      <Col sm={6} xxl={4} key={bookmark.id}>
                        <BookCard
                          bookmark={bookmark}
                          onDelete={handleDelete}
                          onFavouriteToggle={handleFavouriteToggle}
                          onShareToggle={handleShareToggle}
                          onStarToggle={handleStarToggle}
                          onEdit={handleEditClick}
                        />
                      </Col>
                    ))}
                </Row>
              </TabPane>
              <TabPane tabId="tab2">
                <div id="favourite-tab-pane">
                  <Row>
                    {bookmarksData
                      .filter((bookmark) => bookmark.isFavourite)
                      .map((bookmark) => (
                        <Col sm={6} xxl={4} key={bookmark.id}>
                          <BookCard
                            bookmark={bookmark}
                            onDelete={handleDelete}
                            onFavouriteToggle={handleFavouriteToggle}
                          />
                        </Col>
                      ))}
                  </Row>
                </div>
              </TabPane>
              <TabPane tabId="tab3">
                <div id="share-tab-pane">
                  <Row>
                    {bookmarksData
                      .filter((bookmark) => bookmark.isShared)
                      .map((bookmark) => (
                        <Col sm={6} xxl={4} key={bookmark.id}>
                          <BookCard
                            bookmark={bookmark}
                            onDelete={handleDelete}
                            onFavouriteToggle={handleFavouriteToggle}
                            onShareToggle={handleShareToggle}
                            onStarToggle={handleStarToggle}
                            onEdit={function (): void {
                              throw new Error("Function not implemented.");
                            }}
                          />
                        </Col>
                      ))}
                  </Row>
                </div>
              </TabPane>
              <TabPane tabId="tab4">
                <div id="important-tab-pane">
                  <Row>
                    {bookmarksData
                      .filter((bookmark) => bookmark.isStarred)
                      .map((bookmark) => (
                        <Col sm={6} xxl={4} key={bookmark.id}>
                          <BookCard
                            bookmark={bookmark}
                            onDelete={handleDelete}
                            onFavouriteToggle={handleFavouriteToggle}
                            onShareToggle={handleShareToggle}
                            onStarToggle={handleStarToggle}
                          />
                        </Col>
                      ))}
                  </Row>
                </div>
              </TabPane>
              <TabPane tabId="tab5">
                <div id="delet-tab-pane">
                  <Row>
                    {bookmarksData
                      .filter((bookmark) => bookmark.isDelete)
                      .map((bookmark) => (
                        <Col sm={6} xxl={4} key={bookmark.id}>
                          <BookCard
                            bookmark={bookmark}
                            onDelete={handleDelete}
                            onFavouriteToggle={handleFavouriteToggle}
                            onShareToggle={handleShareToggle}
                            onStarToggle={handleStarToggle}
                          />
                        </Col>
                      ))}
                  </Row>
                </div>
              </TabPane>
            </TabContent>
          </Col>
        </Row>

        <Modal isOpen={modalOpen} toggle={toggleModal}>
          <ModalHeader toggle={toggleModal} className="bg-primary text-white">
            New Bookmark
          </ModalHeader>
          <ModalBody>
            <Form className="app-form">
              <FormGroup>
                <Label for="title">Title</Label>
                <Input
                  type="text"
                  id="title"
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                  required
                />
              </FormGroup>
              <FormGroup>
                <Label for="weburl">Url</Label>
                <Input
                  type="text"
                  id="weburl"
                  value={url}
                  onChange={(e) => setUrl(e.target.value)}
                  required
                />
              </FormGroup>
              <FormGroup>
                <Label for="image">Image</Label>
                <Input
                  type="file"
                  id="image"
                  onChange={handleFileChange}
                  required
                />
              </FormGroup>
            </Form>
          </ModalBody>
          <ModalFooter>
            <Button color="secondary" onClick={toggleModal}>
              Close
            </Button>
            <Button color="primary" onClick={handleAddBookmark}>
              Add New
            </Button>
          </ModalFooter>
        </Modal>

        {/* Edit Bookmark Modal */}
        <Modal isOpen={editModalOpen} toggle={toggleEditModal}>
          <ModalHeader
            toggle={toggleEditModal}
            className="bg-primary text-white"
          >
            Edit Bookmark
          </ModalHeader>
          <ModalBody>
            <Form className="app-form">
              <FormGroup>
                <Label for="title">Title</Label>
                <Input
                  type="text"
                  id="title"
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                  required
                />
              </FormGroup>
              <FormGroup>
                <Label for="weburl">Url</Label>
                <Input
                  type="text"
                  id="weburl"
                  value={url}
                  onChange={(e) => setUrl(e.target.value)}
                  required
                />
              </FormGroup>
              <FormGroup>
                <Label for="image">Image</Label>
                <Input type="file" id="image" onChange={handleFileChange} />
              </FormGroup>
            </Form>
          </ModalBody>
          <ModalFooter>
            <Button color="secondary" onClick={toggleEditModal}>
              Close
            </Button>
            <Button color="primary" onClick={handleSaveChanges}>
              Save Changes
            </Button>
          </ModalFooter>
        </Modal>
      </Container>
    </div>
  );
};

export default BookmarkPage;
